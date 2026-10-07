# GameCore Studio: ETOS integration contract and operational setup

**Status:** final reference as of P4.2d (2026-10-06), against **etos main ≥ e4067fd (contains 278ef9c)**. The owner records this upstream merge/push on 2026-10-06; retained build digests remain pinned to their original build. ([P4.3-final §Baseline](packets/P4.3-final-docs.md#baseline)) The companion agent `gamecore-studio` is the only holder of an etos
*agent* key; Unity holds an *app* key. Nothing here bypasses etos's authority model, and nothing in Unity talks
to a model provider.

## 1. Roles and keys

| Principal | Kind | How it is created | What it may do |
|---|---|---|---|
| `gamecore-studio` | installed agent (Rust process supervised by etosd) | `etos agent install studio/etos/agent` (key issued by etosd, rotated on every install/upgrade; `crates/etnode/src/agents/mod.rs:418-439`, `crates/etapi/src/apps.rs:918-943`) | grants: `logger, query, changes, tasks, files, topics, ops, providers, realtime, proxy, services` |
| `gamecore-unity` | installed app + paired key | `etos app install studio/etos/app` (declares `uses = ["gamecore-studio"]`, `routes = ["proxy","query","changes","entrances"]`), then `etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json` (`crates/etapi/src/apps.rs:483-598`; `uses` only via install, `crates/etcli/src/apps.rs:173-188`) | proxied HTTP/WebSocket to the companion, read-only `query` of its own tables |
| Studio workers | etos container workers | `etos worker create gc-designer --image localhost/gc-designer:current --network offline` before agent install; the agent manifest's `[[worker]]` then only sets model/instructions/tools (`crates/etagents/src/manifest.rs:150-171`; image/network preserved on install, `agents/mod.rs:224-259`) | run tasks opened by the companion |

The agent process inherits etosd's environment (`crates/etnode/src/agents/process.rs:293-296`), including provider
keys; the companion never logs its environment and never forwards it to Unity.

## 2. Unity ↔ companion protocol (through etos)

Base: `http://127.0.0.1:7410/api/v1/agents/gamecore-studio/http` with `Authorization: Bearer <app key>` and `X-GameCore-Project: <stable SHA-256>`; etos adds
`X-Etos-App` and `X-Etos-Proxy-Token`, strips `Authorization`, and pipes bodies and upgrades
(`crates/etagents/src/proxy.rs:172-293`). The companion registers its loopback endpoint with
`PUT /agent/endpoint` on every connect (token and endpoint are in memory and reset on etosd restart,
`crates/etagents/src/channel.rs:153-158`) and verifies `X-Etos-Proxy-Token` on every request. WebSocket uses a
ticket: `POST /tickets {path}` → `?etos_ticket=` (single use, 30 s, `crates/etapi/src/tickets.rs:107-139`).

| Method + path (relative to the base) | Purpose | Notes |
|---|---|---|
| `GET /v1/hello` | version, capabilities, provider status (`image`, `tts`, `voice`, `3d`, `describe` each `live\|not_configured\|blocked\|unknown`) | polled on Studio open |
| `POST /v1/requests` | submit an `EditRequest {changeSetId, intent, selection, contextSlice, toolCatalogRevision, toolCatalog?, worker?, attachments[]}` | idempotent on `changeSetId`; returns `{requestId, taskId?, state}` |
| `GET /v1/requests/{id}` | state, task status, outcome | mirrors etos `TaskInfo.status` verbatim plus companion states |
| `POST /v1/requests/{id}/cancel` | cancel | calls `POST /tasks/{task}/cancel`; result is the etos status |
| `GET /v1/requests?after=&limit=` | list for recovery after reload | durable ledger |
| `GET /v1/candidates/{id}` | the validated change set and artifact manifest | artifacts referenced by sha256 |
| `GET /v1/artifacts/{sha256}` | bytes | served from the content store, digest verified on write and read |
| `POST /v1/index/delta` | semantic index delta (nodes, edges, removals, revision) | companion logs `gc_*` traces |
| `POST /v1/ops/generate` | direct media op for a tool (`asset.generate`): `{op: image\|tts\|3d\|describe, spec, max_cost_usd, changeSetId?}` | runs `POST /ops/generate.image` etc.; returns artifact refs |
| `WS /v1/events?after=` | request/task/candidate/voice events, ordered, with cursor | reconnect with `after` |
| `WS /v1/voice` | duplex: client → `{type:"audio", seq, pcm16 base64}` (≤ 24 KiB raw = 32 KiB base64 per frame) / `{type:"stop"}`; server → transcript revisions (`role` always `user`; `final` only on `done`), speech boundaries, errors | one session per Studio instance |
| `POST /v1/stage` / `GET /v1/stage/{job}` | stage a retained candidate; job state | §6 |
| `POST /v1/stage/app-candidate` | authenticated app-origin candidate retention and staging | signed envelope; §6 |
| `GET /v1/stage/{job}/verdict` / `POST /v1/stage/{job}/verify` | fetch signed passing record / verify exact record | §6 |
| `POST /v1/stage/{job}/cancel` | cancel an owned running stage | returns job state after execution teardown; §6 |

Error bodies are etos-shaped `{code, message, hint}`; etos refusal codes pass through unchanged
(`not_configured`, `outcome_unknown`, `request_rejected`, `budget_exhausted`, `too_large`, `agent_starting`,
`forbidden`, …). The exact route and state sources are [api.rs](../../studio/agent/src/api.rs) and [model.rs](../../studio/agent/src/model.rs). The companion adds `candidate_invalid`, `stale_context`, `stage_failed`, `ledger_conflict` and the
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
| `gc-designer` | `localhost/gc-designer:current` (python3, jq, imagemagick) | `offline` (ops run on the node) | `echo/gpt-6-sol` | configure/compose change sets: entities, dialogue, quests, rules, UI bindings, placement; asset generation via `etos generate image`, `etos tts` | `request.md`, `selection.json`, `index-slice.json`, `tool-catalog.json`, `frame.png` | `/outputs/changeset.json` (+ artifacts) |
| `gc-mechanic` | `localhost/gc-mechanic:current` (.NET 8 SDK, prewarmed NuGet, GameCore rules DLLs) | `offline` | `echo/gpt-6-sol` | new mechanism packages (`mechanism.propose`) with `dotnet test` run inside the container before output | same + `package-template/`, `kernel-contracts.md` | `/outputs/package/**`, `/outputs/proposal.json` |

Worker instructions (`studio/etos/agent/workers/*.md`) specify: read the tool catalog first; produce only
operations in the catalog; never invent object ids; cite the `indexRevision`; stop and return `status: needs-clarification`
with at most one question when two interpretations differ materially; never claim an asset you did not write to
`/outputs`. The companion rejects a change set that violates the schema **or the tool catalog** (unknown tool, missing required
args, disallowed target kind, candidate-mode fields) and may send one schema re-ask as a new task, linked in the companion ledger (etos TaskRequest has no native parent).

## 5. Media and voice

| Capability | Provider | P4.2d qualification |
|---|---|---|
| Image | Echo `echo-images`, `gpt-image-2` | Operator tariff USD 0.20/image; prior P4.2c generation/import receipts, full robe texture assignment remains BLOCKED. |
| Describe | Echo chat describe | Available but unpriced; no P4.2d call. |
| TTS | `bailian-tts`, `qwen3-tts-flash` | Published tariff USD 0.0000114682/billable character; two P4.2d calls. |
| Voice | `studio-voice`, Bailian realtime | Ready-gated self-test transcribes both fixtures; later workflow driver FAIL. |
| 3D | No configured provider | `not_configured`, no generation; W-AI-07 PASS is refusal coverage. |

These are observed states, not provider guarantees. Echo Claude credentials were revoked; workers use `echo/gpt-6-sol`. ([P4.2d §Host, installation and authority / Ledger and caps](packets/P4.2d-live-rerun.md), [agent manifest](../../studio/etos/agent/agent.toml))

SDK: `Ops::generate` used to post `generate` instead of `generate.image`; fixed in the pinned etos **etos main ≥ e4067fd (contains 278ef9c)**
(`Ops::generate(family, input)` posts `generate.<family>`), and the companion now uses it (P0.5).

Operation timeout: the companion runs each media op on its own client timeout, `ops_timeout_secs` (default 300 s,
companion `config.toml`), because `generate.image` blocks until done and outlives the SDK's 30 s default. It **holds**
`POST /v1/ops/generate` until the node answers. Past the timeout it answers 504 `transport`, with the op's idempotency
`key` in `hint` and in `data.key`. The client recovers by resending the identical request: same spec and changeSetId
give the same key, and etops answers a known key with the finished result instead of generating again. The etos proxy
between Unity and the companion must allow a request this long.

`max_cost_usd` goes only to `generate.image`, `tts` and `generate.3d`. etops' `describe` input has no such field and
refuses unknown fields, so the companion omits it and refuses `describe` itself when the ceiling is 0 (P0.5).

Partial speech: upstream SDK events use `done`; companion transcript frames use `revision` and optional `final:true`. Only final user text enters the prompt box. ([voice.rs wire contract](../../studio/agent/src/voice.rs))
No voice utterance triggers a tool directly.

### Tariffs and budgets

Provider availability precedes budget validation. A free node status lookup must establish the
requested provider before a media call can return `budget_unpriced`; absent providers return
`not_configured`. Configured operations with no binding tariff remain refused.

A binding tariff is either `published`, with the provider's list-price `url`, or `operator`, with an
explicit non-empty declaration `note`. Both require a positive finite `per_unit` and the correct
quantity unit. An operator estimate binds `max_cost_usd` but is not a published price or an invoice.
Image counts and Alibaba's billable text-character count are included in preflight. Model,
parameter and reference overrides without a matching priced contract remain refused.

`ops.toml` is the installation source of truth: `# @studio source = "operator"` (or `"published"`),
`# @studio note = "..."`, `# @studio url = "https://..."` and `# @studio unit = "..."` are TOML
comment annotations on the enclosing provider. The pinned etops `CostConfig` denies unknown fields;
this versioned comment convention retains provenance without putting metadata in provider options
or weakening its parser. `install-state.py` parses the annotations as TOML and derives companion
`[[ops_prices]]` from that provider's **name**, model and cost. `echo-images` is the image operation
provider; `echo/gpt-image-2` is not an operation-provider alias. TTS uses `bailian-tts` with model
`qwen3-tts-flash`. `models.toml` token prices do not establish a total image-generation tariff.

No Echo list price is claimed. The image template has the owner-declared USD 0.20 total estimate; describe still has a zero/SET_BY_OPERATOR placeholder and refuses independently. A
TTS-only apply, `studio/etos/install.sh --apply-prices --only tts`, leaves other prices untouched.
The TTS source is [Alibaba's Mainland Qwen3-TTS-Flash list price](https://www.alibabacloud.com/help/en/model-studio/model-pricing),
$0.114682 per 10,000 billable characters, checked 2026-10-06.

The R4 companion reloads only tariffs from its operator-owned `state/config.toml` on each hello or
media request. Removing or corrupting that file fails closed. Hello adds
`tariffs: [{op, tariff: {kind, provider, model, unit, perUnitUsd, url, note}}]`; over-budget diagnostics
include `data.tariff`; successful media responses include `charge: {tariff, quantity, costUsd}`.
The companion retains that binding charge in SQLite `media_charges`, keyed by the operation's
idempotency key. This is local ceiling accounting, distinct from the provider's eventual invoice.

P4.2d activated immutable companion `0.1.0-e8a72b2d6eb3aad9` and verified its checksum. File edits do not establish that an older binary or node loaded prices. This documentation packet does not restart either service or call providers; a later operator must verify authenticated hello before a priced call. ([P4.2d §Host, installation and authority](packets/P4.2d-live-rerun.md#host-installation-and-authority))

## 6. Staging (code admission)

R2 uses a candidate-only typed request. All routes require the authenticated proxy app and
`X-GameCore-Project: <stable project SHA-256>`; cross-owner jobs/artifacts/candidates return 404.

```json
{"changeSetId":"cs_...","projectId":"<64 hex>","sourceRevision":"<git commit>","catalogRevision":"<64 hex>"}
```

`POST /v1/stage` resolves the validated candidate and all its proposal/stageInputs from the ledger.
The operator's `[stage.projects]` map resolves `projectId` to an absolute source Unity project;
request-supplied `sourceProject` paths and legacy `packageRef` extraction are refused. Optional
`steps` creates a diagnostic partial run that cannot admit; optional `slot` selects a slot within
this owner's namespace. The response is 202 with `jobId`; `GET /v1/stage/{job}` returns status and
retained `verdictRef` evidence. Discard is `POST /v1/stage {changeSetId,projectId,action:"discard"}`
and holds the slot lock while deleting.

Running-job cancellation is `POST /v1/stage/{job}/cancel` with an empty JSON object, through the
same authenticated app/project proxy. Foreign jobs return 404. Cancellation first records
`cancelling`, prevents verdict issuance, and terminates the owned child/container execution.
Only after execution resources and the slot are released does it return durable `cancelled`.
Terminal jobs are unchanged. Cancelling an HTTP wait or discarding a slot is not job cancellation.
The Studio Stage panel exposes Cancel independently of candidate rejection; cancelled stages
cannot authorize Admit.

`GET /v1/stage/{job}/verdict` returns the companion-issued signed passing record only.
`POST /v1/stage/{job}/verify` accepts that entire record and returns `{verified:true|false}`.
The HMAC binds job/app/project/source/catalog, package/proposal digests, all seven step results,
`confinement`, `coldCache`, and the evidence reference. The installation key stays in companion
state with mode 0600. Unity trusts the authenticated transport and verifier, never candidate or
CAS verdict bytes. Partial, failed, missing-step and unauthenticated records cannot authorize Admit.

App-origin staging uses `CompanionClient.StageAppCandidateAsync`: `POST /v1/stage/app-candidate` carries `{payloadBase64, signature}`, with HMAC over `gamecore.stage.app-candidate/1\n` plus exact UTF-8 payload bytes. The payload contains `app`, `request`, `changeSet`, `toolCatalog`, `files[{bytesBase64}]`; the request contains the four stage identity fields. `X-GameCore-Stage-Key` is transient and authenticated against the node, never logged/persisted. Worker candidates already in the ledger use `StageAsync`. ([app_candidate.rs](../../studio/agent/src/stage/app_candidate.rs), [CompanionClient.cs](../../Packages/com.gamecore.studio.etos/Client/CompanionClient.cs))

Minimal slots run scan, checkers, dotnet (including mandatory Roslyn semantic analysis), Unity
EditMode, PlayMode smoke, determinism, and budget. The warm budget is 360 seconds; cold cache runs
may take longer and record `coldCache:true`. Cache identity includes Unity and kernel/gameplay
package versions. `[stage] confinement="docker"` is the default: no network, no live project or
host credentials, read-only Unity/licences/trusted packages, and only slot/cache writable. Startup
and per-job probe failure yields `stage_failed` with `reason:"sandbox_unavailable"` and no issued
verdict. The explicit operator opt-in `confinement="host"` is recorded in every resulting verdict
and must be shown as a degraded-mode warning by admission UI. There is no automatic fallback.

Unity still runs through `studio/tools/unity-batch.sh` under the shared host allocation protocol.
Its Rust engine wrapper routes stdout/stderr and `-logFile -` through the shared redactor before
writing persistent logs. The analyzer CLI and full wire contract are in
[the R2-F packet](../../studio/agent/evidence/PACKET.md).

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

The Editor publishes a full snapshot on session/project open, then deltas after index rebuilds
and applied/undone change sets. Capture follows engine notifications; one authenticated POST is
in flight at a time. Omitting `baseRevision` denotes a full owner snapshot, including removal of
previously published sources absent from that snapshot. The companion retains source-to-row
inventory so graph edits, node deletion, undo and restart do not leave stale live node rows.

A `dialogue.graph` definition expands its actual `fields.nodes.value` array into
`gc_dialogue_node` records. `gc_definition` exposes `owner`, `asset_guid` and `graph`;
`gc_dialogue_node` exposes the same owner and graph identity. Owner is SHA-256 of the compact
authenticated `[app,projectId]` identity; graph keys are owner-prefixed and based on the graph's
asset GUID. The worker's ordinary `request.md` supplies its owner namespace and query recipe:
resolve the selected NPC's dialogue asset GUID from its index slice, query `gc_definition` for
that owner's `asset_guid`, then query nodes by the returned `graph`. Filter `removed=false` in
both queries. Unfiltered dialogue queries omit only the graph predicate, retaining owner scope.

## 8. Operational setup (host)

Use [10-install-build-run.md](10-install-build-run.md) for the host sequence. The Studio node is `~/.local/share/etos-studio`, API `127.0.0.1:7410`, broker `172.17.0.1:7411`, UI `127.0.0.1:7400`. It runs as the existing user. `bin/gamecore-studio` is the manifest command; current installation copies an immutable release rather than following a mutable build symlink. ([P0.1 §1](packets/P0.1-host-etos.md), [P0.5 §R3-C and follow-ups](packets/P0.5-companion.md), [agent manifest](../../studio/etos/agent/agent.toml))

The required upstream line is **etos main ≥ e4067fd (contains 278ef9c)**. `studio/etos/etos.lock` is machine-written build provenance (commit plus binary digests), not editable documentation; its original 278ef9c build must not be relabeled as rebuilt e4067fd. Vendor source follows that recorded build. ([lock](../../studio/etos/etos.lock), [P4.3-final §Baseline](packets/P4.3-final-docs.md#baseline))

Provider keys stay in operator-owned environment references; Unity settings store only the key-file path and node URL. Logs/UI/evidence use the shared redaction policy, including token families and recursive secret-key values. No raw credentials are printed. ([R2-A §R2 fixes](packets/R2-A-core-edit-recovery.md#r2-fixes), [R2-D §R2 fixes](packets/R2-D-studio-etos-client.md#r2-fixes))

## 9. What is not done through etos (and why)

- Picking, previews, undo and local application: editor-local, no model (D2).
- Staging compile/tests: scheduled by the host companion inside its Docker sandbox with trusted project inputs; etos
  workers only produce the package.
- Player builds and launches: Unity build pipeline on the host.

## 10. Timeouts, cursors and protocol sources

| Boundary | Default / contract | Source |
|---|---|---|
| Ordinary C# HTTP | 60 s | [EtosClientOptions](../../Packages/com.gamecore.studio.etos/Client/EtosClientOptions.cs) |
| C# media / artifact | 360 s / 180 s | Same source |
| Companion task open / media | 180 s / 300 s | [config.rs](../../studio/agent/src/config.rs) |
| Stage lane | 360 s warm; first cold grace 1,800 s; recorded allowance, never silently widened | [pipeline.rs cold_budget](../../studio/agent/src/stage/pipeline.rs) |
| Legacy stage process wrapper | 900 s default; distinct from lane budget | [config.rs](../../studio/agent/src/config.rs) |
| Admission target | 90 s, not met by P4.2d | [P4.2d §Stage and admission](packets/P4.2d-live-rerun.md#stage-and-admission) |
| Events | ack cursor after main-thread handle; replay without resubmission | [R2-D §R2 fixes](packets/R2-D-studio-etos-client.md#r2-fixes) |

The exact companion member definitions are [model.rs](../../studio/agent/src/model.rs); route/method/header enforcement is [api.rs](../../studio/agent/src/api.rs). Hello negotiates minimum client contract 2/revision 4635746 and exposes providers, workers and tariff provenance. JSON schemas under [schemas](schemas/) define the authoring payloads, not every HTTP envelope. Schema drift must be fixed by the generating owner, never by hand-editing those files. ([P4.2d §Host, installation and authority](packets/P4.2d-live-rerun.md#host-installation-and-authority))
