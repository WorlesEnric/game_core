//! A fake etos node for the companion's integration tests: the routes the companion uses,
//! served by axum on a loopback port, with the node's semantics where the companion depends
//! on them (read in etos `6c2c3f4`):
//!
//! - `GET /api/v1/agent/connect` (WebSocket): `hello` → `welcome {proxy_token}`; `kick()` drops
//!   every channel (the SDK reconnects and gets the current token).
//! - `PUT /agent/endpoint` (answer carries the proxy token), `PUT /services/{agent}`.
//! - `POST /tasks` (idempotent on `id`; the request is the first record of the topic, posted
//!   by `agent:<agent>`), `GET /tasks/{id}`, `POST /tasks/{id}/cancel`.
//! - `GET /topics/{topic}/records?after=&limit=&wait_ms=` (long poll).
//! - `POST /files?name=&media_type=`, `GET /files/{ref}`.
//! - `POST /ops/{op}`: scripted answers per op (`status` lists providers).
//! - `GET /realtime/connect?provider=` (WebSocket): ready, gapless `audio-<seq>` checks, a
//!   partial transcript after the first chunk, the final transcript and `closed` on `close`.
//! - `PUT /bindings/{app}`, `POST /bindings/{app}/traces` (traces kept).
//! - `POST /tickets`, `ANY /agents/{agent}/http/{*rest}`: the proxy (app key → `X-Etos-App`,
//!   `X-Etos-Proxy-Token`, `Authorization` removed).
#![allow(dead_code, clippy::unwrap_used, clippy::expect_used)]

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use axum::Router;
use axum::body::{Body, Bytes};
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, Method, StatusCode, Uri};
use axum::response::{IntoResponse, Response};
use axum::routing::{any, get, post, put};
use base64::Engine as _;
use futures::{SinkExt, StreamExt};
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use tokio::sync::{Notify, broadcast};

pub const AGENT: &str = "gamecore-studio";
pub const APP_KEY: &str = "etk_app_key_for_tests_0001";
pub const AGENT_KEY: &str = "etk_agent_key_for_tests_01";

#[derive(Debug, Clone)]
pub struct Task {
    pub id: String,
    pub request: String,
    pub worker: String,
    pub topic: String,
    pub text: String,
    pub inputs: Vec<String>,
    pub status: String,
}

#[derive(Debug, Clone)]
pub struct File {
    pub name: String,
    pub media: String,
    pub bytes: Vec<u8>,
}

#[derive(Default)]
pub struct Inner {
    pub token: String,
    pub endpoint: Option<String>,
    pub endpoint_puts: usize,
    pub service_puts: usize,
    pub hellos: usize,
    pub ready: usize,
    /// `(method, path, query)` of every HTTP request.
    pub log: Vec<(String, String, String)>,
    pub tasks: Vec<Task>,
    pub topics: HashMap<String, Vec<Value>>,
    /// `(topic, after)` of every topic read.
    pub topic_reads: Vec<(String, u64)>,
    pub files: HashMap<String, File>,
    pub ops: HashMap<String, (u16, Value)>,
    /// `(op, body)` of every operation call.
    pub op_calls: Vec<(String, Value)>,
    pub binding: Option<Value>,
    pub traces: Vec<Value>,
    pub trace_posts: usize,
    /// Realtime commands' action types, in order.
    pub rt_actions: Vec<String>,
    /// Decoded size of each audio chunk.
    pub rt_chunks: Vec<usize>,
    /// Audio payload digests for byte-preservation assertions.
    pub rt_audio_digests: Vec<String>,
    pub rt_errors: Vec<String>,
    /// Model VAD that emits no transcript until explicit input commit.
    pub rt_needs_commit: bool,
    /// Task request ids `POST /tasks` refuses (403 `request_rejected`).
    pub refuse_ids: Vec<String>,
    /// Operations whose produced file is reported with a wrong digest.
    pub wrong_digest_ops: Vec<String>,
    /// The next N task opens record the task, then answer only after `open_delay_ms` (as
    /// etos does when it launches the task inside `POST /tasks` on a loaded host).
    pub slow_opens: usize,
    pub open_delay_ms: u64,
    /// `error` reported by `GET /tasks/{id}` (e.g. a degraded model).
    pub task_error: Option<String>,
    /// Operations answer only after this long (a slow image provider).
    pub op_delay_ms: u64,
}

#[derive(Clone)]
pub struct FakeNode {
    pub url: String,
    pub inner: Arc<Mutex<Inner>>,
    notify: Arc<Notify>,
    kick: broadcast::Sender<()>,
    counter: Arc<AtomicU64>,
}

fn sha(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}

fn refusal(status: u16, code: &str, message: &str) -> Response {
    (
        StatusCode::from_u16(status).unwrap(),
        axum::Json(json!({"code": code, "message": message})),
    )
        .into_response()
}

fn now() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap()
        .as_millis() as i64
}

impl FakeNode {
    pub async fn start() -> FakeNode {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let url = format!("http://{}", listener.local_addr().unwrap());
        let (kick, _) = broadcast::channel(4);
        let mut inner = Inner {
            token: "etp_first_token_000001".into(),
            ..Inner::default()
        };
        inner.ops.insert(
            "status".into(),
            (
                200,
                json!({"providers": {"image": ["echo-images"], "describe": ["echo-chat"], "tts": [], "3d": []},
                       "absent": ["tts", "3d"]}),
            ),
        );
        let node = FakeNode {
            url,
            inner: Arc::new(Mutex::new(inner)),
            notify: Arc::new(Notify::new()),
            kick,
            counter: Arc::new(AtomicU64::new(1)),
        };
        let app = Router::new()
            .route("/api/v1/agent/connect", get(connect))
            .route("/api/v1/agent/endpoint", put(endpoint))
            .route("/api/v1/services/{agent}", put(services))
            .route("/api/v1/tasks", post(open_task))
            .route("/api/v1/tasks/{id}", get(get_task))
            .route("/api/v1/tasks/{id}/cancel", post(cancel_task))
            .route("/api/v1/topics/{*path}", get(read_topic))
            .route("/api/v1/files", post(put_file))
            .route("/api/v1/files/{id}", get(get_file))
            .route("/api/v1/ops/{op}", post(op))
            .route("/api/v1/realtime/connect", get(realtime))
            .route("/api/v1/bindings/{app}", put(binding))
            .route("/api/v1/bindings/{app}/traces", post(traces))
            .route("/api/v1/tickets", post(ticket))
            .route("/api/v1/agents/{agent}/http/{*rest}", any(proxy))
            .layer(axum::middleware::from_fn_with_state(node.clone(), record))
            .with_state(node.clone());
        tokio::spawn(async move {
            axum::serve(listener, app).await.unwrap();
        });
        node
    }

    fn next(&self, prefix: &str) -> String {
        format!("{prefix}{}", self.counter.fetch_add(1, Ordering::SeqCst))
    }

    pub fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner.lock().unwrap()
    }

    pub fn token(&self) -> String {
        self.lock().token.clone()
    }

    /// A new proxy token (as after an etosd restart) and every agent channel dropped.
    pub fn restart(&self, token: &str) {
        self.lock().token = token.into();
        let _ = self.kick.send(());
    }

    pub fn tasks(&self) -> Vec<Task> {
        self.lock().tasks.clone()
    }

    pub fn calls(&self, method: &str, path: &str) -> usize {
        self.lock()
            .log
            .iter()
            .filter(|(m, p, _)| m == method && p == path)
            .count()
    }

    pub fn file(&self, id: &str) -> File {
        self.lock().files.get(id).cloned().unwrap()
    }

    /// Store bytes as a pinned reference (as a worker's output would be).
    pub fn put(&self, name: &str, bytes: &[u8]) -> String {
        let id = self.next("ref_");
        self.lock().files.insert(
            id.clone(),
            File {
                name: name.into(),
                media: "application/octet-stream".into(),
                bytes: bytes.to_vec(),
            },
        );
        id
    }

    pub fn post_record(
        &self,
        topic: &str,
        sender: &str,
        text: &str,
        status: Option<&str>,
        refs: Vec<String>,
    ) {
        self.post_record_with(topic, sender, text, status, refs, Value::Null);
    }

    /// [`FakeNode::post_record`] with the record's `data`.
    pub fn post_record_with(
        &self,
        topic: &str,
        sender: &str,
        text: &str,
        status: Option<&str>,
        refs: Vec<String>,
        data: Value,
    ) {
        {
            let mut g = self.lock();
            let records = g.topics.entry(topic.to_string()).or_default();
            let pos = records.len() as u64 + 1;
            let mut r = json!({"pos": pos, "sender": sender, "at": now(), "text": text});
            if let Some(s) = status {
                r["status"] = json!(s);
            }
            if !refs.is_empty() {
                r["refs"] = json!(refs);
            }
            if !data.is_null() {
                r["data"] = data;
            }
            records.push(r);
        }
        self.notify.notify_waiters();
    }

    /// The worker finishes `task` with output files: a progress record, then the final record.
    pub fn complete(&self, task: &str, outputs: &[(&str, Vec<u8>)]) {
        let t = self
            .lock()
            .tasks
            .iter()
            .find(|t| t.id == task)
            .cloned()
            .unwrap();
        let refs: Vec<String> = outputs.iter().map(|(n, b)| self.put(n, b)).collect();
        let sender = format!("{}@fake", t.worker);
        self.post_record(&t.topic, &sender, "working on it", Some("working"), vec![]);
        if let Some(x) = self.lock().tasks.iter_mut().find(|x| x.id == task) {
            x.status = "done".into();
        }
        self.post_record(&t.topic, &sender, "done", Some("done"), refs);
    }

    pub fn set_op(&self, op: &str, status: u16, answer: Value) {
        self.lock().ops.insert(op.into(), (status, answer));
    }

    /// Wait until `f` holds (5 s).
    pub async fn until(&self, what: &str, f: impl Fn(&Inner) -> bool) {
        let ok = tokio::time::timeout(Duration::from_secs(10), async {
            loop {
                if f(&self.lock()) {
                    return;
                }
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        })
        .await;
        assert!(ok.is_ok(), "timed out waiting for {what}");
    }
}

async fn record(
    State(n): State<FakeNode>,
    req: axum::extract::Request,
    next: axum::middleware::Next,
) -> Response {
    let method = req.method().to_string();
    let path = req.uri().path().trim_start_matches("/api/v1").to_string();
    let query = req.uri().query().unwrap_or_default().to_string();
    n.lock().log.push((method, path, query));
    next.run(req).await
}

fn agent_auth(headers: &HeaderMap) -> bool {
    headers.get("authorization").and_then(|v| v.to_str().ok())
        == Some(&format!("Bearer {AGENT_KEY}"))
}

// ---------------------------------------------------------------------------------------------
// Agent channel.

async fn connect(State(n): State<FakeNode>, headers: HeaderMap, ws: WebSocketUpgrade) -> Response {
    if !agent_auth(&headers) {
        return refusal(401, "unauthorized", "bad key");
    }
    ws.on_upgrade(move |socket| channel(n, socket))
}

async fn channel(n: FakeNode, socket: WebSocket) {
    let (mut tx, mut rx) = socket.split();
    let mut kick = n.kick.subscribe();
    let Some(Ok(Message::Text(hello))) = rx.next().await else {
        return;
    };
    let hello: Value = serde_json::from_str(hello.as_str()).unwrap();
    assert_eq!(hello["type"], "hello");
    assert_eq!(hello["agent"], AGENT);
    let token = {
        let mut g = n.lock();
        g.hellos += 1;
        g.token.clone()
    };
    let welcome = json!({"type": "welcome", "agent": AGENT, "node": "fake", "sdk": "1.0.0", "proxy_token": token});
    if tx
        .send(Message::Text(welcome.to_string().into()))
        .await
        .is_err()
    {
        return;
    }
    loop {
        tokio::select! {
            m = rx.next() => match m {
                Some(Ok(Message::Text(t))) => {
                    let v: Value = serde_json::from_str(t.as_str()).unwrap_or(Value::Null);
                    if v["type"] == "ready" {
                        n.lock().ready += 1;
                    }
                }
                Some(Ok(_)) => {}
                _ => return,
            },
            _ = kick.recv() => {
                let _ = tx.send(Message::Close(None)).await;
                return;
            }
        }
    }
}

async fn endpoint(State(n): State<FakeNode>, headers: HeaderMap, body: Bytes) -> Response {
    if !agent_auth(&headers) {
        return refusal(401, "unauthorized", "bad key");
    }
    let v: Value = serde_json::from_slice(&body).unwrap();
    let url = v["url"].as_str().unwrap().to_string();
    assert!(url.starts_with("http://127.0.0.1:"), "loopback endpoint");
    let mut g = n.lock();
    g.endpoint = Some(url.clone());
    g.endpoint_puts += 1;
    axum::Json(json!({"agent": AGENT, "url": url, "proxy_token": g.token})).into_response()
}

async fn services(State(n): State<FakeNode>, Path(agent): Path<String>, body: Bytes) -> Response {
    let v: Value = serde_json::from_slice(&body).unwrap();
    n.lock().service_puts += 1;
    let methods: Vec<String> = v["methods"].as_object().unwrap().keys().cloned().collect();
    axum::Json(json!({"agent": agent, "version": 1, "changed": true, "methods": methods}))
        .into_response()
}

// ---------------------------------------------------------------------------------------------
// Tasks and topics.

fn task_info(t: &Task) -> Value {
    json!({"task": t.id, "worker": t.worker, "topic": t.topic, "status": t.status, "created_at": 1})
}

fn task_info_with(n: &FakeNode, t: &Task) -> Value {
    let mut v = task_info(t);
    if let Some(e) = n.lock().task_error.clone() {
        v["error"] = json!(e);
    }
    v
}

async fn open_task(State(n): State<FakeNode>, body: Bytes) -> Response {
    let v: Value = serde_json::from_slice(&body).unwrap();
    let request = v["id"].as_str().unwrap().to_string();
    let topic = v["topic"].as_str().unwrap().to_string();
    if !topic.starts_with(&format!("#agent/{AGENT}/")) {
        return refusal(403, "not_yours", "not your topic");
    }
    let worker = v["worker"].as_str().unwrap().to_string();
    if worker == "nobody" {
        return refusal(403, "not_yours", "not a worker of the agent");
    }
    if n.lock().refuse_ids.contains(&request) {
        return refusal(403, "request_rejected", "the node refused this task");
    }
    if let Some(t) = n.lock().tasks.iter().find(|t| t.request == request) {
        return axum::Json(task_info(t)).into_response();
    }
    let task = Task {
        id: n.next("t"),
        request: request.clone(),
        worker,
        topic: topic.clone(),
        text: v["text"].as_str().unwrap().to_string(),
        inputs: v["inputs"]
            .as_array()
            .unwrap()
            .iter()
            .map(|x| x.as_str().unwrap().to_string())
            .collect(),
        status: "queued".into(),
    };
    let info = task_info(&task);
    n.lock().tasks.push(task);
    n.post_record(
        &topic,
        &format!("agent:{AGENT}"),
        v["text"].as_str().unwrap(),
        None,
        vec![],
    );
    let delay = {
        let mut g = n.lock();
        if g.slow_opens > 0 {
            g.slow_opens -= 1;
            g.open_delay_ms
        } else {
            0
        }
    };
    if delay > 0 {
        tokio::time::sleep(Duration::from_millis(delay)).await;
    }
    axum::Json(info).into_response()
}

async fn get_task(State(n): State<FakeNode>, Path(id): Path<String>) -> Response {
    let task = n.lock().tasks.iter().find(|t| t.id == id).cloned();
    match task {
        Some(t) => axum::Json(task_info_with(&n, &t)).into_response(),
        None => refusal(404, "not_found", "no such task"),
    }
}

/// As the real node: the node first tells the worker's controller, which at once posts a final
/// `failed` record ("This task was cancelled ...", refusal code `cancelled`) on the origin
/// topic; only then does the node mark the task cancelled and answer.
async fn cancel_task(State(n): State<FakeNode>, Path(id): Path<String>) -> Response {
    let open = {
        let g = n.lock();
        match g.tasks.iter().find(|t| t.id == id) {
            Some(t) => (!matches!(t.status.as_str(), "done" | "failed" | "cancelled"))
                .then(|| (t.topic.clone(), format!("{}@fake", t.worker))),
            None => return refusal(403, "not_yours", "this agent did not open the task"),
        }
    };
    if let Some((topic, sender)) = open {
        let text = "This task was cancelled (cancelled by the owner); I stopped working on it.";
        n.post_record_with(
            &topic,
            &sender,
            text,
            Some("failed"),
            vec![],
            json!({"refusal": {"code": "cancelled", "message": text}}),
        );
        // The node's own cancel (container, queue) takes a moment: the follower sees the
        // record first.
        tokio::time::sleep(Duration::from_millis(300)).await;
    }
    let mut g = n.lock();
    let Some(t) = g.tasks.iter_mut().find(|t| t.id == id) else {
        return refusal(403, "not_yours", "this agent did not open the task");
    };
    if !matches!(t.status.as_str(), "done" | "failed" | "cancelled") {
        t.status = "cancelled".into();
    }
    axum::Json(task_info(t)).into_response()
}

async fn read_topic(
    State(n): State<FakeNode>,
    Path(path): Path<String>,
    Query(q): Query<HashMap<String, String>>,
) -> Response {
    let Some(name) = path.strip_suffix("/records") else {
        return refusal(404, "no_route", "no route");
    };
    let topic = if name.starts_with('#') {
        name.to_string()
    } else {
        format!("#{name}")
    };
    let after: u64 = q.get("after").and_then(|s| s.parse().ok()).unwrap_or(0);
    let limit: usize = q.get("limit").and_then(|s| s.parse().ok()).unwrap_or(100);
    let wait = Duration::from_millis(q.get("wait_ms").and_then(|s| s.parse().ok()).unwrap_or(0));
    n.lock().topic_reads.push((topic.clone(), after));
    let deadline = tokio::time::Instant::now() + wait;
    loop {
        let notified = n.notify.notified();
        let (records, head) = {
            let g = n.lock();
            let all = g.topics.get(&topic).cloned().unwrap_or_default();
            let head = all.len() as u64;
            let records: Vec<Value> = all
                .into_iter()
                .filter(|r| r["pos"].as_u64().unwrap() > after)
                .take(limit)
                .collect();
            (records, head)
        };
        if !records.is_empty() || tokio::time::Instant::now() >= deadline {
            return axum::Json(json!({"records": records, "head": head})).into_response();
        }
        let _ = tokio::time::timeout_at(deadline, notified).await;
    }
}

// ---------------------------------------------------------------------------------------------
// Files and ops.

async fn put_file(
    State(n): State<FakeNode>,
    Query(q): Query<HashMap<String, String>>,
    body: Bytes,
) -> Response {
    let name = q.get("name").cloned().unwrap_or_else(|| "file".into());
    let media = q
        .get("media_type")
        .cloned()
        .unwrap_or_else(|| "application/octet-stream".into());
    let id = n.next("ref_");
    let digest = format!("sha256:{}", sha(&body));
    n.lock().files.insert(
        id.clone(),
        File {
            name: name.clone(),
            media: media.clone(),
            bytes: body.to_vec(),
        },
    );
    axum::Json(
        json!({"id": id, "name": name, "kind": "pinned", "size": body.len(), "owner": "fake",
                      "media_type": media, "digest": digest}),
    )
    .into_response()
}

async fn get_file(State(n): State<FakeNode>, Path(id): Path<String>) -> Response {
    match n.lock().files.get(&id) {
        Some(f) => (StatusCode::OK, f.bytes.clone()).into_response(),
        None => refusal(404, "not_found", "no such reference"),
    }
}

async fn op(State(n): State<FakeNode>, Path(op): Path<String>, body: Bytes) -> Response {
    let v: Value = serde_json::from_slice(&body).unwrap_or(Value::Null);
    n.lock().op_calls.push((op.clone(), v.clone()));
    // As etops: describe's input schema has no `max_cost_usd` (unknown fields are refused).
    if op == "describe" && v.get("max_cost_usd").is_some() {
        return refusal(
            400,
            "bad_request",
            "invalid describe input: unknown field `max_cost_usd`",
        );
    }
    let delay = n.lock().op_delay_ms;
    if delay > 0 {
        tokio::time::sleep(Duration::from_millis(delay)).await;
    }
    let scripted = n.lock().ops.get(&op).cloned();
    match scripted {
        Some((200, answer)) if op.starts_with("generate.") || op == "tts" => {
            // A finished job: one produced file, shared as a reference.
            let bytes = answer["bytes"]
                .as_str()
                .unwrap_or("PNGDATA")
                .as_bytes()
                .to_vec();
            let name = answer["name"].as_str().unwrap_or("image-1.png").to_string();
            let id = n.put(&name, &bytes);
            let digest = if n.lock().wrong_digest_ops.contains(&op) {
                format!("sha256:{}", sha(b"something else"))
            } else {
                format!("sha256:{}", sha(&bytes))
            };
            let media = answer["media_type"]
                .as_str()
                .unwrap_or("image/png")
                .to_string();
            axum::Json(json!({"key": v["key"], "op": op, "provider": v["provider"], "job_id": "job_1",
                              "state": {"state": "succeeded"},
                              "refs": [{"id": id, "name": name, "kind": "pinned", "size": bytes.len(), "owner": "fake",
                                        "media_type": media, "digest": digest}]}))
            .into_response()
        }
        Some((200, answer)) => axum::Json(answer).into_response(),
        Some((status, answer)) => {
            (StatusCode::from_u16(status).unwrap(), axum::Json(answer)).into_response()
        }
        None => refusal(
            404,
            "not_configured",
            "this operation is not available on this node: no provider is configured",
        ),
    }
}

// ---------------------------------------------------------------------------------------------
// Realtime.

async fn realtime(
    State(n): State<FakeNode>,
    Query(q): Query<HashMap<String, String>>,
    ws: WebSocketUpgrade,
) -> Response {
    let provider = q.get("provider").cloned().unwrap_or_default();
    if provider == "http-refused" {
        // As etos does for an admission refusal: an HTTP answer instead of the upgrade.
        return refusal(
            503,
            "not_configured",
            "no realtime adapter is registered for this provider",
        );
    }
    ws.on_upgrade(move |socket| realtime_session(n, socket, provider))
}

async fn realtime_session(n: FakeNode, socket: WebSocket, provider: String) {
    let (mut tx, mut rx) = socket.split();
    let Some(Ok(Message::Text(cfg))) = rx.next().await else {
        return;
    };
    let cfg: Value = serde_json::from_str(cfg.as_str()).unwrap();
    if provider != "studio-voice" {
        let _ = tx
            .send(Message::Text(
                json!({"error": {"code": "not_configured", "message": "no realtime adapter is registered for this provider"}})
                    .to_string()
                    .into(),
            ))
            .await;
        return;
    }
    assert_eq!(cfg["audio_format"], "pcm16");
    assert_eq!(cfg["sample_rate_hz"], 24000);
    let (sid, generation) = (cfg["session_id"].clone(), cfg["generation"].clone());
    let frame = |event: Value| {
        json!({"session_id": sid, "generation": generation, "event": event}).to_string()
    };
    let ready = frame(
        json!({"type": "ready", "capabilities": ["duplex_audio", "manual_response", "context_replace",
        "input_transcript", "speech_detection"], "audio_format": "pcm16", "sample_rate_hz": 24000}),
    );
    tx.send(Message::Text(ready.into())).await.unwrap();
    let mut next_audio = 0u64;
    while let Some(Ok(Message::Text(t))) = rx.next().await {
        let c: Value = serde_json::from_str(t.as_str()).unwrap();
        let kind = c["action"]["type"].as_str().unwrap_or_default().to_string();
        n.lock().rt_actions.push(kind.clone());
        match kind.as_str() {
            "input_audio" => {
                let seq = c["action"]["sequence"].as_u64().unwrap();
                let bytes = base64::engine::general_purpose::STANDARD
                    .decode(c["action"]["audio"].as_str().unwrap())
                    .unwrap();
                if seq != next_audio
                    || c["id"] != format!("audio-{seq}")
                    || bytes.is_empty()
                    || bytes.len() > 32 * 1024
                    || !bytes.len().is_multiple_of(2)
                {
                    n.lock().rt_errors.push(format!("bad chunk {seq}"));
                    let _ = tx.send(Message::Text(frame(json!({"type": "error", "code": "bad_realtime_command", "message": "gap"})).into())).await;
                    return;
                }
                next_audio += 1;
                n.lock().rt_chunks.push(bytes.len());
                n.lock().rt_audio_digests.push(sha(&bytes));
                if seq == 0 && !n.lock().rt_needs_commit {
                    tx.send(Message::Text(
                        frame(json!({"type": "speech_started", "item_id": "it1"})).into(),
                    ))
                    .await
                    .unwrap();
                    tx.send(Message::Text(
                        frame(
                            json!({"type": "transcript", "role": "user", "item_id": "it1",
                        "response_id": null, "revision": 0, "text": "give the", "done": false}),
                        )
                        .into(),
                    ))
                    .await
                    .unwrap();
                }
            }
            "close" if n.lock().rt_needs_commit => {
                let _ = tx.close().await;
                return;
            }
            "close" | "input_commit" => {
                for ev in [
                    json!({"type": "speech_ended", "item_id": "it1"}),
                    json!({"type": "transcript", "role": "assistant", "item_id": "it2", "response_id": null,
                           "revision": 0, "text": "(assistant speech)", "done": true}),
                    json!({"type": "transcript", "role": "user", "item_id": "it1", "response_id": null,
                           "revision": 1, "text": "give the ferryman a lantern", "done": true}),
                    json!({"type": "closed", "reason": "closed by the client"}),
                ] {
                    tx.send(Message::Text(frame(ev).into())).await.unwrap();
                }
                let _ = tx.close().await;
                return;
            }
            other => n
                .lock()
                .rt_errors
                .push(format!("unexpected command {other}")),
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Logger, tickets, proxy.

async fn binding(State(n): State<FakeNode>, Path(app): Path<String>, body: Bytes) -> Response {
    let v: Value = serde_json::from_slice(&body).unwrap();
    assert!(v["digest"].as_str().unwrap().starts_with("sha256:"));
    n.lock().binding = Some(v);
    axum::Json(json!({"app": app, "version": 1, "changed": true})).into_response()
}

async fn traces(State(n): State<FakeNode>, body: Bytes) -> Response {
    let v: Value = serde_json::from_slice(&body).unwrap();
    let list = v["traces"].as_array().unwrap().clone();
    let count = list.len();
    let mut g = n.lock();
    g.trace_posts += 1;
    g.traces.extend(list);
    axum::Json(json!({"accepted": count, "duplicates": 0, "refused": []})).into_response()
}

async fn ticket(headers: HeaderMap, body: Bytes) -> Response {
    if headers.get("authorization").and_then(|v| v.to_str().ok())
        != Some(&format!("Bearer {APP_KEY}"))
    {
        return refusal(401, "unauthorized", "bad key");
    }
    let v: Value = serde_json::from_slice(&body).unwrap();
    assert!(
        v["path"]
            .as_str()
            .unwrap()
            .starts_with(&format!("/api/v1/agents/{AGENT}/http/"))
    );
    axum::Json(json!({"ticket": "ett_ticket_for_tests_01", "expires_in": 30})).into_response()
}

async fn proxy(
    State(n): State<FakeNode>,
    Path((agent, rest)): Path<(String, String)>,
    method: Method,
    uri: Uri,
    headers: HeaderMap,
    body: Bytes,
) -> Response {
    if headers.get("authorization").and_then(|v| v.to_str().ok())
        != Some(&format!("Bearer {APP_KEY}"))
    {
        return refusal(401, "unauthorized", "bad key");
    }
    if agent != AGENT {
        return refusal(404, "agent_unknown", "no such agent");
    }
    let (endpoint, token) = {
        let g = n.lock();
        (g.endpoint.clone(), g.token.clone())
    };
    let Some(endpoint) = endpoint else {
        return refusal(503, "no_endpoint", "no listener");
    };
    let query = uri.query().map(|q| format!("?{q}")).unwrap_or_default();
    let url = format!("{endpoint}/{rest}{query}");
    let client = reqwest::Client::new();
    let mut req = client.request(method, &url).body(body.to_vec());
    for (k, v) in headers.iter() {
        if !matches!(
            k.as_str(),
            "authorization" | "host" | "x-etos-app" | "x-etos-proxy-token"
        ) {
            req = req.header(k, v);
        }
    }
    let res = req
        .header("x-etos-app", "gamecore-unity")
        .header("x-etos-proxy-token", token)
        .send()
        .await
        .unwrap();
    let status = res.status();
    let ct = res.headers().get("content-type").cloned();
    let bytes = res.bytes().await.unwrap();
    let mut out = Response::new(Body::from(bytes));
    *out.status_mut() = status;
    if let Some(ct) = ct {
        out.headers_mut().insert("content-type", ct);
    }
    out
}
