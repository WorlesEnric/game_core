# etos-sdk (Rust)

Agents and apps on etos, in Rust ([design-agents.md §5](../../docs/design/design-agents.md)).
It depends on no core crate: its wire types are written by hand and checked against the node's
JSON Schema by `tests/schema.rs`, through the SDKs' copy of it (`sdk/ts/schema/etapi.json`,
which the core's `types` gate keeps equal to `crates/etapi/schema/etapi.json`). It is its own Cargo
workspace (the core's root `Cargo.toml` excludes `sdk/rust`); agents under `agents/` depend on
it by path.

Dependencies: tokio, reqwest (rustls), tokio-tungstenite (rustls, webpki roots), serde,
serde_json, thiserror, futures, sha2, hex, base64, getrandom, tracing.

## Connecting

```rust
let client = etos_sdk::Client::from_env()?;          // ETOS_URL, ETOS_KEY_FILE, ETOS_AGENT
let client = etos_sdk::Client::new("http://127.0.0.1:7070", &key)?; // an app
```

Every error is an `Error`: `Refused { status, code, message, hint }` (the node's refusal),
`Transport` (unreachable, timed out, cut off), `Protocol` (an answer the SDK does not
understand) or `Invalid` (refused locally before anything was sent). Only safe calls are
retried (reads, idempotent registrations, deduplicated traces), after a transport error, 408,
429 or 5xx, with backoff from 200 ms (`with_retries`, `with_backoff`) and `Retry-After`
honoured up to 60 s. Service calls, operations, task opening, topic publishing and file uploads
are never retried by the SDK.

## Agents

```rust
use etos_sdk::{Agent, Client, Refusal, Reply, Retry, ServiceSpec, ToolSpec};

let agent = Agent::connect(Client::from_env()?).await?;
agent.tool("say", ToolSpec::new("Repeat a text").retry(Retry::Safe), |call| async move {
    call.event("working", serde_json::json!({}));
    tokio::select! {
        _ = call.cancelled() => Err(Refusal::new("cancelled", "stopped as asked")),
        r = work(call.args()) => Ok(Reply::text(&r)),
    }
}).await?;
agent.service("echo", ServiceSpec::new().deadline_ms(2_000), |call| async move {
    Ok(Reply::data(serde_json::json!({"user": call.context().user})))
}).await?;
agent.serve().await?; // reports ready; returns when the node sends `shutdown`
```

The complete program is [examples/echo_agent.rs](examples/echo_agent.rs).

| Item | What it does |
|---|---|
| `Agent::connect(client)`, `connect_with(client, AgentOptions)` | Opens `GET /api/v1/agent/connect` (WebSocket, `Authorization: Bearer <key>`) and sends `hello {sdk: "etos-sdk/rust/<version>", agent}`, then waits up to 10 s for the node's `welcome`. A refused key, or the node's `refused` (wrong agent, SDK version), is returned here as `Error::Refused` (403) and is not retried. |
| `agent.welcome()` → `Option<Welcome>` | The node's `welcome {agent, node, sdk}` of the current connection; `welcome.proxy_token()` is the token proxied requests carry. |
| `agent.tool(name, ToolSpec, handler)` | `PUT /tools/<agent>.<name> {description, input_schema, retry, timeout_s}`, then handles calls of kind `tool`. |
| `agent.service(method, ServiceSpec, handler)` | `PUT /services/<agent> {methods: {name: {input_schema, deadline_ms, apps?}}}` (every method so far), then handles calls of kind `service`; the reply's `data` is the app's response body. |
| `agent.serve()` | Sends `ready` and serves until `shutdown` (`Ok`), a local `close()` (`Ok`) or a refusal on reconnect (`Err`). |
| `agent.endpoint(url)` | `PUT /agent/endpoint {url}`; returns the `ProxyToken` (the answer's `proxy_token`, the same as the welcome's). `token.check(header)` compares in constant time; `token.check_headers(&headers)` returns the calling app (`X-Etos-App`) when `X-Etos-Proxy-Token` is right. |
| `Call` | `id`, `kind`, `name` (short), `args`, `args_as::<T>()` (refusal `invalid_args`), `context()` (`task`, `worker`, `app`, `user`), `deadline()`, `event(status, detail)`, `cancelled()`, `is_cancelled()`. |
| Handlers | `Fn(Call) -> impl Future<Output = Result<Reply, Refusal>>`. `Reply { text, refs, data }`; a `Refusal { code, message, hint }` becomes `{ok: false, error}`. An SDK `Error` converts into a `Refusal` with `?`. |

**Channel guarantees.**
- Pings (`{"type":"ping"}`) are answered with `pong`; a connection with no message from the node
  for `idle_timeout` (60 s) is dropped and reopened.
- A dropped connection is reopened with jittered backoff (250 ms doubling to 30 s). After a
  reconnect the SDK sends `hello`, registers its tools and services again (idempotent) and
  sends `ready` if `serve` was called.
- **Redelivery.** A call delivered again with the same id while it is still running joins the
  running call (one answer, on the current connection). A call delivered again after it
  finished is answered from the per-id result cache (4096 results, 15 minutes). So one process
  never runs a call twice. The cache holds every call's result, not only `safe` tools': for a
  `safe` tool it is the deduplication the design asks of the agent; for other calls it only
  prevents a second run if the node delivers them again.
- `cancel` sets the call's cancellation; so does its `deadline_ms`. The handler decides when to
  stop; its answer is still sent (the node discards late answers).
- Unknown tools and methods are answered with `unknown_tool` / `unknown_method`. Unknown
  message types are ignored (newer nodes may add some).
- `shutdown` cancels the calls in flight and ends `serve`.

**Not guaranteed.** Progress events are best-effort (dropped while disconnected). The result
cache lives in memory: after the agent process restarts, a redelivered `safe` call runs again
(which is why it must be safe), and a `never` call is the node's "outcome unknown".

### The other agent routes

| Item | Route |
|---|---|
| `client.ops().decide/rank/choose/ocr/transcribe/describe/generate/tts/search(input)`, `.call(op, input)`, `.for_task(task)` | `POST /ops/{op}` with the operation's JSON input; `for_task` sends `?task=<id>` to charge the task. |
| `providers::url(&client, provider)` | `ProviderUrl { base_url: <node>/api/v1/providers/<provider>, header: "Authorization", value: "Bearer <key>" }`; append the provider's own path. No request is made. |
| `client.changes(after, tables)` → `Changes`, `.wait(duration)`, `.app(app)` (only that app's changes) | `GET /changes?after=&tables=a,b&wait_ms=25000`, answered `ChangePage {changes: [ChangeEntry {pos, app, table, partition, key, at}], next}`; the position moves to `next`. `next_change()`, `page()`, `position()`, `into_stream()`. |
| `client.tasks().open(&TaskRequest {worker, text, topic, inputs, id})` → `TaskInfo`, `.get(id)` | `POST /tasks` (retried only when `id` is set), `GET /tasks/{id}`. |
| `client.topics().publish(topic, &TopicPost {text, data, refs, status, id})` → `TopicAck`, `.read(topic, after, limit, wait)` → `TopicPage` | `POST /topics/{topic}/records`, `GET /topics/{topic}/records?after=&limit=&wait_ms=`. |
| `client.files().put(name, media_type, bytes)` → `FileInfo`, `.get(ref)` → bytes | `POST /files?name=&media_type=` with the raw bytes, `GET /files/{ref}` (raw bytes). |

These routes are not yet in `etapi.json`; the shapes above are the SDK's reading of
design-agents §5.5–5.6 and are loosely typed (JSON values) where the node's answer is not fixed
yet.

## Apps

| Item | What it does |
|---|---|
| `client.logger(LoggerOptions::new(app, states).user(u).store(FileStore::new(path)))` | Declares the binding (`PUT /bindings/{app}`, digest `sha256` of the canonical JSON of the states) and starts background delivery. `log.change(kind, key, values, ChangeMeta)` checks the values against the states and buffers a trace; `with_user(u)`, `flush()`, `close()`, `producer()`. |
| `client.register_source(id, SourceRegistration, concurrency, handler)` | `PUT /sources/{id}`, then long-polls `GET /sources/{id}/requests` and answers `POST /sources/{id}/responses`. Each request is checked against the declared capabilities (collection, projection, operators and value types per field, sort, limit, cursor, parameters) and gets the scope condition added to its filter before the handler sees it; an invalid request is refused without calling the handler. `SourceHandle::stop()`. |
| `client.entrance(name).ask(Ask {..})` → conversation id; `.conversation(id).events(after)` | `POST /entrances/{name}/requests` (idempotent on the request id), then a stream of events, each once and in order, until the conversation is done. |
| `client.query(&Query::sql(..).param("user", "u1").user(..))` | `POST /query` with typed bind parameters (`params`). An agent adds `.app(app)` to query as one app that uses it, and `.describe()` to plan the SQL without running it (`QueryResponse::plan`: placeholder names and the tables read). |
| `client.call_service(agent, method, &body)` | `POST /services/{agent}/{method}`: the base an agent's own client SDK wraps. |

**Logger guarantees.** Exactly once, as in `sdk/ts`: each trace gets the logger's producer id
and the next sequence number when a delivery round starts; the next number is saved in the
`ProducerStore` before the traces are sent, so a restarted process never reuses a number the
node has seen; a failed delivery is retried with the same numbers and the node deduplicates on
(producer, seq). A batch the node refuses permanently is dropped and its refusal returned by
`flush`. Traces still buffered when the process dies are lost: call `close()`. Not
implemented here: private-state routing to a second (local) node and backfill (both in
`sdk/ts`).

## Tests

`cargo test` in `sdk/rust`: unit tests for the pure parts, `tests/agent.rs` and `tests/app.rs`
against a local fake node (axum, HTTP and WebSocket), `tests/schema.rs` against the committed
schema.
