# P0.5 companion — `gamecore-studio`

The packet plan puts this file at the branch root, but this packet may write only under
`studio/agent/**` and `studio/etos/agent/**`, so it lives here.

**Branch:** `worktree-agent-a87161a756b3b10dc`, based on main `7a9c409`.
**etos pin:** `6c2c3f4ea238bd211f9c3e8e9a813b22a13bc9a8`. It is recorded in `ETOS_PIN`, and `build.rs` warns (without failing) when `../../../etos` is at a different commit.
**Toolchain:** Rust 1.97.1. The crate has its own Cargo workspace.

## 1. What was built

`gamecore-studio` is a Rust process that etosd supervises as an installed agent. It bridges the Unity editor (through the node's proxy) to etos tasks, files, topics, ops, realtime and the Resource Graph. It follows docs/studio/02 (boundary D, SADR-002/003/005/018/020) and 04.

| Area | Where | What it does |
|---|---|---|
| Lifecycle | `src/app.rs`, `src/main.rs` | Startup order: ledger, then a loopback HTTP listener (port from `config.port`, else `$ETOS_STATE_DIR/port`, else ephemeral; the result is saved), then `Agent::connect_with`, then the minimal `status` service, then `PUT /agent/endpoint`. A 500 ms monitor PUTs the endpoint again after each reconnect. Shutdown is graceful on SIGTERM/SIGINT. |
| Proxy checks | `src/api.rs::authenticate` | `X-Etos-Proxy-Token` is checked against the **current** welcome token (the SDK re-registers on reconnect and drops the returned token). A mismatch gets 403 `forbidden`. `X-Etos-App` must be in `allowed_apps` (default `gamecore-unity`), else 403. Before the first welcome the answer is 503 `agent_starting`. |
| Unity API | `src/api.rs`, `src/model.rs` | 04 §2 (see §4 below). JSON is camelCase serde mirrors of 03. Errors are `{code, message, hint}`, and etos refusals pass through unchanged (status, code, message, hint). The only companion-specific codes are `candidate_invalid`, `stale_context`, `stage_failed` and `ledger_conflict`. |
| Ledger | `src/ledger.rs` | SQLite (WAL, `synchronous=FULL`). Tables: requests, attempts, candidates, artifacts, events, voice sessions, stage jobs, tool catalogs. Requests are idempotent by `changeSetId`: the same body returns the stored request, a different body gets 409 `ledger_conflict`. **Terminal states are final.** Startup resumes open requests and never reopens a task. |
| Task desk | `src/desk.rs`, `src/candidate.rs`, `src/schema.rs` | **Open:** packs `request.md`, `selection.json`, `index-slice.json` (capped at 2 MiB, with a truncation note), `tool-catalog.json` and attachments; uploads them; opens the task with `id = changeSetId` on `#agent/gamecore-studio/cs-<id lowercased>`. **Follow:** long-polls the topic. **Finish:** on the final record, fetches every ref, identifies documents by content, and checks the JSON Schema plus the structural rules and each artifact's sha256 and size against the delivered bytes. Valid artifacts are stored content-addressed and a `candidate` event is emitted. **Cancel** goes through etos. `waiting` is surfaced and never retried. An invalid candidate gets **one** automatic re-ask (`<cs>.r1`, parent task recorded, `diagnostics.json` attached), then it is terminal. |
| Media ops | `src/ops.rs` | `ops().call("generate.image" \| "tts" \| "generate.3d" \| "describe")`. It never uses `Ops::generate`, which posts `generate`. The idempotency key is `gc-<sha40>` over changeSetId, op, spec and max cost. Outputs are fetched, stored, indexed (`gc_asset`) and announced with an `asset` event. Hello reports each provider from the free `status` op (live / not_configured / blocked / unknown), cached 60 s. |
| Voice | `src/voice.rs` | Transcription only. `realtime().open("studio-voice", pcm16/24000)`. Client audio of any size is re-chunked to ≤24 KiB raw (≤32 KiB base64) with gapless `audio-<seq>`. It never sends `response_create`, and audio/response events are dropped. A refusal reaches the client as `{type:"error", code, message}` followed by `closed`. |
| Index logger | `src/index.rs` | Declares 10 global `gc_*` states under the agent's own binding. Rows are coalesced per (kind, key), and a ticker sends **≤1 batch/s**. Rows are flushed on shutdown. Removals become `removed: true`. Node `type` is the `[Authorable]` type id (`npc.definition`, `dialogue.graph`, …). |
| Stage runner | `src/stage.rs` | Runs `studio/stage/stage.sh <slot> <package-dir>`: the package is extracted from the store, `env_clear` plus an allowlist is applied, and a timeout is enforced. The verdict is the last JSON line on stdout. A missing script gives 503 `stage_failed{hint}`. |
| Logging | `src/redact.rs` | `tracing` to stderr through a redacting writer (removes `etk_/ett_/etp_/eta_` tokens and `Bearer …`). The environment is never logged. The stage child gets an allowlisted env with secret-like names removed. |
| Manifest | `studio/etos/agent/agent.toml` | `command = "bin/gamecore-studio"`. Grants: `logger, query, changes, tasks, files, topics, ops, providers, realtime, proxy, services`. `providers = ["studio-voice"]`. Workers `gc-designer` and `gc-mechanic` use `model = "default"`. |
| Workers | `studio/etos/agent/workers/*.md` | Per 04 §4: read the tool catalog first, produce only catalog ops, never invent ids, copy `selection.json`, write `/outputs/changeset.json` (with the P0.3 schema fields only) with an artifacts sha256 list, ask at most one clarification question via `/outputs/clarification.json`, never claim unwritten assets. The mechanic also uses the 03 §8 package layout, runs `dotnet test` first, and writes `proposal.json`, `package.tgz` and a `mechanism.propose` change set. |

### P0.3 contract alignment (coordinator update, applied)

- `schemas/change-set.schema.json` is now a **byte-for-byte copy** of P0.3's `docs/studio/schemas/change-set.schema.json`, taken from branch `worktree-agent-a904de072adf44bb1`. `build.rs` warns if the two diverge once both are on one branch.
- Change-set ids follow the contract: `^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$`. `POST /v1/requests` refuses any other id with 400. Otherwise every task would end in `candidate_invalid`.
- Candidate findings use the registered code **`CandidateInvalid`**, and the message names the rule (`artifact_digest_mismatch: …`). `where` holds only an op id or an AuthoringRef; JSON pointers go in the message.
- The companion's structural rules match the Unity `ChangeSetValidator` for:
  - schema;
  - id;
  - no operations;
  - duplicate op id;
  - unknown dependency;
  - dependency cycle;
  - invalid or duplicate artifact digest;
  - malformed artifact reference;
  - unlisted reference;
  - **unused artifact**.

  It adds two checks the editor cannot make: `artifact_digest_mismatch` and `artifact_size_mismatch` against the delivered bytes. It does not check the catalog, targets or requirements; the editor's validator does.
- TargetId (`StableNameKeyDerivation.Derive("auth." + authoringId)`): the companion never derives TargetIds, so nothing needed mirroring.
- The outer API outcome code stays `candidate_invalid` (snake_case, etos-shaped, per the brief). The PascalCase code applies to the diagnostics inside it.

## 2. Verification

### Fake-node suite (`tests/companion.rs`, `tests/support/mod.rs`)

The fake node is an axum server. It implements the agent WebSocket and welcome, endpoint, services, tasks, topics (long poll), files, scripted ops, realtime, bindings and traces, tickets, and the proxy with its app-key check and headers.

- **13/13 pass**, five consecutive runs at about 0.6 s each.
- **26** library unit tests pass.
- `cargo fmt --check` and `cargo clippy --all-targets -- -D warnings` are clean.

| Test | Covers |
|---|---|
| `request_task_candidate_with_verified_artifacts` | request → task (inputs, topic, id) → final record → sha256 verified → candidate, artifact served, `output_unlisted` warning, events WS replay |
| `digest_mismatch_is_reasked_once_with_parent` | sha failure → one re-ask (`.r1`, parent, `diagnostics.json`) → candidate |
| `invalid_candidate_without_reask_is_terminal` | sha failure with re-ask off → `candidate_invalid` with diagnostics |
| `cancel_reports_the_etos_status` | cancel through etos. **The node's closing `failed` record does not overwrite `cancelled`** (regression from the real run, below) |
| `restart_resumes_from_the_ledger_without_reopening` | companion restart mid-task: resumed from the cursor, no second `POST /tasks` |
| `voice_session_ready_transcripts_and_close` | ready, gapless 24576/24576/10848-byte chunks, transcripts, `close`. Only `input_audio` and `close` actions are sent |
| `voice_refusal_passes_through` | refusal as an error frame, and an HTTP-refused upgrade (node code recovered) |
| `proxy_token_and_app_are_checked_and_follow_restarts` | 403 without/with a wrong token, 403 for a wrong app, ticketed WS, token rotation after an etosd restart |
| `requests_are_idempotent_and_conflicts_refused` | same body gives the same task; a different body gives 409 `ledger_conflict`; `stale_context`; held catalog |
| `etos_task_refusal_passes_through` | `not_yours` 403 passes through unchanged |
| `ops_generate_stores_artifacts_and_passes_refusals` | `generate.image` stored and indexed; `not_configured` passes through |
| `index_deltas_are_coalesced_and_delivered` | ≤1 batch/s, coalescing, flush on shutdown |
| `stage_shell_runs_the_command_or_reports_stage_failed` | missing script gives `stage_failed{hint}`; script verdict |

### Real etosd on the Mac (`tests/real_node.rs`, ignored by default)

- **Node:** etosd/etos 0.2.0 built from the pinned commit. Root `~/.cache/gc-p05/node`, because the host-socket path must be shorter than `SUN_LEN` and the scratchpad path was too long.
- **Settings:** `[api] listen = 127.0.0.1:7461`, `[containers] runtime = "none"` (Docker Desktop on this Mac fails to start), no `ops.toml`.
- **Model:** a local fixture model (`models.toml` alias `default` → a Python OpenAI-compatible stub that waits 6 s and answers text). No provider keys were used and nothing paid was called.

Steps:
```
etos agent install --link studio/etos/agent   # with bin/gamecore-studio -> ../../../agent/target/release/gamecore-studio
  installed gamecore-studio 0.1.0 (GameCore Studio companion), linked
  created worker gc-designer
  created worker gc-mechanic
etos agent list
  gamecore-studio 0.1.0 ready restarts 0
etos app install <test dir>/app       # TEMPORARY app.toml (studio/etos/app/app.toml absent): app = "gamecore-unity",
                                      # routes = ["proxy","query","changes","entrances"], uses = ["gamecore-studio"]
etos app pair gamecore-unity --approve --out ~/.cache/gc-p05/app-key.json
STUDIO_REAL_APP_KEY=... STUDIO_REAL_ETOS=... cargo test --test real_node -- --ignored --nocapture --test-threads 1
  test result: ok. 2 passed
```
The full transcript is in `evidence/real-node-transcript.txt` and the agent log in `evidence/real-node-agent-log.txt`. Neither contains a key or token (checked by grep).

**Exercised for real** (app key → etosd proxy → companion → etosd agent routes):

1. `GET /v1/hello` through the proxy returns 200 with `app: gamecore-unity`, `node: studio`, `sdk: 1.0.0`, workers, and providers `image/tts/3d/describe: not_configured` (from the real `status` op) and `voice: unknown`.
2. A WebSocket to `/v1/events?after=0` opened with a **ticket** (`POST /api/v1/tickets`, no Authorization header). It received `request`, `task_progress` and `candidate_invalid` events.
3. `POST /v1/requests` opened a real task (`tc5de7…`) on `#agent/gamecore-studio/cs-…`. The same body again returned the same task id.
4. The hostless worker finished with text and no outputs, so the companion reported `candidate_invalid` (`CandidateInvalid` / `changeset_missing`). It **re-asked once** (attempt 1, new task, parent recorded) and then settled as terminal `candidate_invalid`, with `tasks` holding both ids.
5. Cancel gave `cancelled`, taskStatus `cancelled`, and it **stayed** cancelled.
   - In the first real run (before the fix) the node's closing `failed` record ("This task was cancelled…") overwrote it to `failed`. That request still shows `failed` in the ledger.
   - The fix: the ledger refuses to change a terminal request, and the follower exits once the request is settled. The fake node now behaves like the real node here.
6. **Companion restart mid-task** (`etos agent restart`): the proxy answered 503 `agent_starting` for 0.3 s, then the new process resumed the request from the ledger. The first task was kept, never reopened. It then re-asked and settled.
7. **etosd restart:** the node stopped and restarted the agent. It came back on the same port (persisted) with a new proxy token, and hello through the proxy answered 200. All 8 requests were intact.
8. Direct calls to the loopback endpoint without the proxy token, or with a wrong one, get 403 `forbidden`.
9. A voice WebSocket through the proxy with a ticket: the real node refuses `studio-voice` (no adapter). The client gets `{type:"error", code:"not_configured", message:"no realtime adapter is registered for this provider"}` then `closed{reason:"refused"}`.
   - The SDK alone reports only `HTTP error: 503`.
   - The companion recovers the node's body with one plain handshake that is refused again at admission. See open item 2.
10. `POST /v1/ops/generate {op:"3d"}` returns 503 `not_configured` with the node's hint, unchanged.
11. `POST /v1/index/delta` was delivered. With the **agent** key, `SELECT … FROM gc_entity` returns the Ferryman row and `gc_changeset` holds the requests' states. With the **app** key the answer is `unknown_table`, as etos intends (an app queries only its own tables; 04 §1 says "read-only query of its own tables").

**Not exercised on the real node** (covered only by the fake node, or not at all):

- **A valid candidate end to end** with a real worker writing `/outputs/changeset.json` and assets. Hostless tasks (runtime `none`) have no write/shell tool, and Docker Desktop would not start, so no real fetch-and-verify of output refs against a real final record ran on the Mac. The Linux build host (myubuntu, with podman/docker) is where P0.1 should run it.
- `waiting` (budget) records.
- A successful media op. No providers and no keys, and paid calls are forbidden from the Mac.
- A successful voice session and transcripts (no realtime provider).
- The stage runner on a real node (`studio/stage/stage.sh` does not exist yet).
- The `describe` op.
- Workers querying `gc_*` through `etos query` from inside a task.
- Token rotation **without** a process restart: etosd restarts its agents, so the reconnect path inside one process was seen only on the fake node.

## 3. Open items and suggested etos patches (P0.1 owns etos)

1. **`Ops::generate` bug** (`sdk/rust/src/client/ops.rs`): it posts op `generate` instead of `generate.image`. The companion avoids it with `ops().call`. Suggested patch: map the call to `generate.image`, or deprecate the method.
2. **Realtime refusal body is lost** (`sdk/rust` realtime `open`): an admission refusal answered over HTTP becomes `Error::Protocol("realtime connection failed: HTTP error: 503 …")`. Suggested patch: read the response body and return `Error::Refused{status, code, message, hint}`. Until then the companion makes one extra plain handshake to read the code (`voice.rs::probe_refusal`).
3. **`TaskRequest` has no `parent`.** The re-ask's parent is recorded in the ledger and named in `request.md` and `diagnostics.json`. Suggested patch: an optional `parent` that etos records on the task.
4. **04 §8's `command = "../../agent/target/release/gamecore-studio"` is invalid**, because etos refuses `..` in agent paths. The manifest uses `bin/gamecore-studio`, so **P0.1's `install.sh` must create** `studio/etos/agent/bin/gamecore-studio -> ../../../agent/target/release/gamecore-studio` (or copy the binary) before `etos agent install --link`. `bin/` is git-ignored.
5. **Mechanic outputs are flattened.** etos shares outputs by basename (≤64 files), so the package travels as `/outputs/package.tgz`. The stage runner extracts tar/tgz, picking the format from the magic bytes.
6. Voice provider status is `unknown` in hello until the first session, because no cheap realtime probe exists without opening a session.
7. The media-op idempotency key covers (changeSetId, op, spec, max cost). Repeating an identical request returns the first result. For a variation, the client changes the spec (e.g. a `seed`) or the changeSetId.
8. The `IndexNode` in the companion's delta API accepts two optional extensions, `rgKind` and `rgKey`. P0.3's `semantic-index.schema.json` node has `additionalProperties: false`, so P2.2 must strip them before validating a node against that schema, or not send them. Deltas also accept `baseRevision`, `projectInfo` and `removals`.
9. **Worktree path dependency:** `etos-sdk = { path = "../../../etos/sdk/rust" }` resolves next to the repo root. In this worktree a sibling symlink `.claude/worktrees/etos` points at a `git clone --shared` of etos at the pin. P0.1 works in the main etos checkout on branch `studio/bailian-tts`, which is at a different commit, so `build.rs` prints its pin warning there.
10. The Unity app cannot query `gc_*` with its key (point 11 above). P2.2 reads the index through its own index or the companion. If P2.2 needs RG reads from the editor, a companion `GET /v1/index/query` passthrough is a small addition.

## 4. API summary for P2.2 (all under `/api/v1/agents/gamecore-studio/http` on the node, app key)

| Method / path | Body → answer |
|---|---|
| `GET /v1/hello` | → `{service, version, protocol: 1, node, sdk, app, connected, workers, providers: {image,tts,3d,describe,voice: live\|not_configured\|blocked\|unknown}, providersCheckedAt, indexRevision, toolCatalogRevisions, capabilities}` |
| `POST /v1/requests` | `EditRequest {changeSetId (cs_ULID), intent{text, origin, voiceTranscriptId?}, selection (SelectionSnapshot), contextSlice (index slice), toolCatalogRevision, toolCatalog? (send it when the companion does not hold that revision, else 409 stale_context), worker? (gc-designer default), attachments?: [{name, mediaType, role, data: base64, sha256?}] (≤8, ≤16 MiB each)}` → `{requestId, changeSetId, taskId, state, taskStatus, request: RequestView}`. Idempotent by changeSetId; a different body gets 409 `ledger_conflict`. |
| `GET /v1/requests?after=<seq>&limit=` | → `{requests: [RequestView], next}` |
| `GET /v1/requests/{id}` | → `RequestView {requestId, changeSetId, worker, state: requested\|running\|waiting\|candidate\|candidate_invalid\|needs_clarification\|failed\|cancelled\|unresolved, taskId, taskStatus, topic, attempt, tasks[], outcome?, hasCandidate, seq, createdAt, updatedAt}` |
| `POST /v1/requests/{id}/cancel` | → RequestView (cancelled through etos; a no-op once terminal) |
| `GET /v1/candidates/{id}` | → `{changeSetId, taskId, attempt, changeSet (as written), artifacts: [{sha256, name, mediaType, bytes, producer, role, url}], diagnostics (warnings), receivedAt}` |
| `GET /v1/artifacts/{sha256}` | → bytes (Content-Type = mediaType) |
| `POST /v1/index/delta` | `{project, revision, baseRevision?, projectInfo?, nodes: [IndexNode], edges, removals}` → `{revision, queued, skipped}` |
| `POST /v1/ops/generate` | `{op: image\|tts\|3d\|describe, spec, max_cost_usd, changeSetId?}` → `{op, etosOp, provider, state, artifacts, text, key}`. etos refusals pass through. |
| `POST /v1/stage` | `{changeSetId, packageRef: sha256 of a stored artifact}` → 202 `StageJobView {jobId, changeSetId, packageRef, state, slot, verdict, createdAt, updatedAt}`. With no script: 503 `stage_failed`. |
| `GET /v1/stage/{job}` | → StageJobView |
| WS `/v1/events?after=<cursor>` | Ticket required: `POST /api/v1/tickets {path}`, then `?etos_ticket=`. Replays from the ledger, then live. Frames are `{cursor, at, type, requestId, data}` with `type` one of `request` (every state change, `waiting` included), `task_progress` (working, waiting, failed, done), `candidate`, `candidate_invalid`, `clarification`, `asset`, `voice_session`, `voice_transcript`, `stage`. Pings every 20 s. |
| WS `/v1/voice` | Ticket required. The server sends `ready{sessionId, audioFormat: pcm16, sampleRateHz: 24000, maxChunkBytes: 24576}`. The client sends `{type:"audio", seq, pcm16: base64}` (any size, seq gapless) and `{type:"stop"}`. The server sends `transcript{role, itemId, revision, text, done}`, `speech_started`, `speech_ended`, `usage`, `error{code, message}` and `closed{reason}`. One session at a time (`rate_limited`). |

Errors are always `{code, message, hint?}`. Etos refusals keep their status and code. The companion's own codes are `candidate_invalid`, `stale_context` (409), `stage_failed` (503) and `ledger_conflict` (409), plus `bad_request`, `not_found`, `forbidden`, `agent_starting` and `too_large`. Diagnostics inside outcomes and candidates follow 03 §9 (`CandidateInvalid`, rule-named message).

## 5. Expectations for P0.1 (install)

- **Build:** `cargo build --release --manifest-path studio/agent/Cargo.toml`. It needs `../etos` next to the game_core checkout at the pin (or accept the warning). The build takes about 1 minute after deps; the first build compiles 182 crates.
- **Link:** `ln -sfn ../../../agent/target/release/gamecore-studio studio/etos/agent/bin/gamecore-studio`, then `etos agent install --link studio/etos/agent`. The workers are created by the install (`created worker gc-designer`, `gc-mechanic`); they need a model aliased `default` in `models.toml`.
- **App:** `studio/etos/app/app.toml` must declare `app = "gamecore-unity"`, `routes = ["proxy","query","changes","entrances"]` and `uses = ["gamecore-studio"]` (the temporary copy used here). Then `etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json`.
- **Optional `$ETOS_STATE_DIR/config.toml`:** unknown keys are an error. Keys: `allowed_apps`, `workers`, `default_worker`, `reask_on_invalid`, `port`, `schema`, `follow_wait_ms`, `hello_cache_s`, `index_flush_ms`, `max_slice_bytes`, `[stage] command/slots/timeout_s`, `[voice] provider/instructions/max_sessions`. Environment overrides: `GAMECORE_STUDIO_ALLOWED_APP`, `GAMECORE_STUDIO_STAGE_COMMAND`, `GAMECORE_STUDIO_SCHEMA`, `GAMECORE_STUDIO_PORT`.
- **Voice:** a `studio-voice` realtime provider must exist on the node (P0.1's provider work). Without it, voice is refused with the node's code, as shown above.
- **Run on a node with a container runtime** to exercise a real candidate. `tests/real_node.rs` is the script: set `STUDIO_REAL_APP_KEY`, and optionally `STUDIO_REAL_ETOS` for the restart scenario.
