#![allow(clippy::unwrap_used, clippy::expect_used)]
//! The companion against a REAL etosd, through the node's proxy with the paired app key, as
//! the Unity client (P2.2) will call it. Ignored by default; run with
//!
//! ```sh
//! STUDIO_REAL_APP_KEY=~/.config/gamecore-studio/app-key.json \
//! STUDIO_REAL_ETOS=/path/to/etos   # optional: enables the restart scenario (etos agent restart)
//! cargo test --test real_node -- --ignored --nocapture --test-threads 1
//! ```
//!
//! It prints a transcript. It never asks for paid work: `/v1/ops/generate` is called only
//! with `STUDIO_REAL_ALLOW_OPS=1`, and the voice session sends 0.2 s of silence.
//!
//! A designer request passes only when it ends `candidate` or `needs_clarification`: a
//! `failed` request (a refusal at `phase: open`, a failed task turn, a degraded model), an
//! invalid candidate or an unresolved one fails the test and prints the outcome and the
//! refusal. (Before etos `5fa113b` this test "passed" while every request had failed at
//! `phase: open`.) `STUDIO_REAL_SETTLE_S` (default 600) bounds the wait.

use std::time::{Duration, Instant};

use base64::Engine as _;
use futures::{SinkExt, StreamExt};
use serde_json::{Value, json};
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;

const AGENT: &str = "gamecore-studio";

struct Node {
    url: String,
    key: String,
    http: reqwest::Client,
}

fn node() -> Option<Node> {
    let path = std::env::var("STUDIO_REAL_APP_KEY").ok()?;
    let path = path.replacen('~', &std::env::var("HOME").unwrap_or_default(), 1);
    let v: Value = serde_json::from_slice(&std::fs::read(path).ok()?).ok()?;
    Some(Node {
        url: v["url"].as_str()?.trim_end_matches('/').to_string(),
        key: v["key"].as_str()?.to_string(),
        http: reqwest::Client::new(),
    })
}

fn say(what: &str, v: &Value) {
    println!("--- {what}\n{}", serde_json::to_string_pretty(v).unwrap());
}

fn settle_limit() -> Duration {
    Duration::from_secs(
        std::env::var("STUDIO_REAL_SETTLE_S")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(600),
    )
}

/// A designer request must produce something the creator can act on.
fn assert_productive(id: &str, settled: &Value) {
    let state = settled["state"].as_str().unwrap_or_default();
    if matches!(state, "candidate" | "needs_clarification") {
        return;
    }
    let outcome = &settled["outcome"];
    let refusal = &outcome["refusal"];
    panic!(
        "{id} ended `{state}`, not `candidate` or `needs_clarification`\n  outcome code: {}\n  phase: {}\n  message: {}\n  refusal: {}\n  reason: {}\n  model error: {}\n  diagnostics: {}\n  full: {settled}",
        outcome["code"],
        outcome["phase"],
        outcome["message"],
        if refusal.is_null() {
            "(none)".to_string()
        } else {
            refusal.to_string()
        },
        outcome["reason"],
        outcome["modelError"],
        outcome["diagnostics"],
    );
}

impl Node {
    fn base(&self) -> String {
        format!("{}/api/v1/agents/{AGENT}/http", self.url)
    }

    async fn call(&self, method: reqwest::Method, path: &str, body: Option<Value>) -> (u16, Value) {
        let mut req = self
            .http
            .request(method, format!("{}{path}", self.base()))
            .bearer_auth(&self.key);
        if let Some(b) = body {
            req = req.json(&b);
        }
        let res = req.send().await.unwrap();
        let status = res.status().as_u16();
        let bytes = res.bytes().await.unwrap();
        (
            status,
            serde_json::from_slice(&bytes).unwrap_or(Value::Null),
        )
    }

    async fn ticketed_ws(
        &self,
        path: &str,
        query: &str,
    ) -> tokio_tungstenite::WebSocketStream<tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>>
    {
        let full = format!("/api/v1/agents/{AGENT}/http{path}");
        let t: Value = self
            .http
            .post(format!("{}/api/v1/tickets", self.url))
            .bearer_auth(&self.key)
            .json(&json!({"path": full}))
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        let ticket = t["ticket"]
            .as_str()
            .unwrap_or_else(|| panic!("no ticket: {t}"));
        let sep = if query.is_empty() { "" } else { "&" };
        let url = format!(
            "{}{full}?{query}{sep}etos_ticket={ticket}",
            self.url.replacen("http", "ws", 1)
        );
        let (ws, _) = tokio_tungstenite::connect_async(url.into_client_request().unwrap())
            .await
            .unwrap();
        ws
    }

    async fn until_settled(&self, id: &str, within: Duration) -> Value {
        let start = Instant::now();
        let mut last = String::new();
        loop {
            let (_, v) = self
                .call(reqwest::Method::GET, &format!("/v1/requests/{id}"), None)
                .await;
            let line = format!(
                "{} attempt={} task={} status={}",
                v["state"], v["attempt"], v["taskId"], v["taskStatus"]
            );
            if line != last {
                println!("  [{:>5.1}s] {line}", start.elapsed().as_secs_f32());
                last = line;
            }
            let state = v["state"].as_str().unwrap_or_default();
            if matches!(
                state,
                "candidate"
                    | "candidate_invalid"
                    | "needs_clarification"
                    | "failed"
                    | "cancelled"
                    | "unresolved"
            ) {
                return v;
            }
            assert!(start.elapsed() < within, "{id} did not settle: {v}");
            tokio::time::sleep(Duration::from_millis(250)).await;
        }
    }
}

/// A request the catalog can satisfy: the slice names the ferryman and a lantern item, and
/// `inventory.grantStarting` grants an existing item. (With an empty slice, or an ask for
/// dialogue the catalog has no tool for, a worker rightly answers `needs_clarification`.)
fn request(id: &str) -> Value {
    json!({
        "changeSetId": id,
        "intent": {"text": "Give the ferryman the lantern as a starting item", "origin": "agent"},
        "selection": {"id": "sel_01J9ZQ00000000000000000001", "mode": "Edit", "indexRevision": 1,
                      "targets": [{"kind": "Entity", "authoringId": "e-ferryman"}]},
        "contextSlice": {"revision": 1, "project": "p05-real", "nodes": [
            {"ref": {"kind": "Entity", "authoringId": "e-ferryman"}, "type": "npc.definition",
             "name": "Ferryman"},
            {"ref": {"kind": "Definition", "authoringId": "item.lantern"}, "type": "item.definition",
             "name": "Lantern"}
        ], "edges": []},
        "toolCatalogRevision": gamecore_studio::util::catalog_revision(&catalog()),
        "toolCatalog": catalog()
    })
}

fn catalog() -> Value {
    let mut c = bare_catalog();
    let revision = gamecore_studio::util::catalog_revision(&c);
    c["revision"] = json!(revision);
    c
}

fn bare_catalog() -> Value {
    json!({"schema": "gamecore.studio.toolcatalog/1", "objectTypes": [], "tools": [
        {"id": "inventory.grantStarting", "tier": "Configure", "runtimeApply": "Live", "targetRequired": true,
         "targetKinds": ["Entity"], "args": [{"name": "item", "type": "ref", "required": true}]}
    ]})
}

fn stamp() -> u128 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap()
        .as_millis()
}

#[tokio::test]
#[ignore = "needs a real etosd with gamecore-studio installed (STUDIO_REAL_APP_KEY)"]
async fn real_node_end_to_end() {
    let Some(n) = node() else {
        panic!("set STUDIO_REAL_APP_KEY to the paired app key file");
    };
    // 1. hello through the proxy.
    let (s, hello) = n.call(reqwest::Method::GET, "/v1/hello", None).await;
    say(&format!("GET /v1/hello through the proxy -> {s}"), &hello);
    assert_eq!(s, 200);
    assert_eq!(hello["app"], "gamecore-unity");

    // 2. Ticketed WebSocket of events, from now.
    let (_, list) = n
        .call(
            reqwest::Method::GET,
            "/v1/requests?after=0&limit=1000",
            None,
        )
        .await;
    let mut ws = n.ticketed_ws("/v1/events", "after=0").await;
    println!("--- WS /v1/events?after=0 opened with a ticket (no Authorization header)");

    // 3. request -> task -> (hostless) final record -> evaluation -> re-ask -> settled.
    let id = format!("cs_0{:025}", stamp() * 10 + 1);
    let (s, v) = n
        .call(reqwest::Method::POST, "/v1/requests", Some(request(&id)))
        .await;
    say(&format!("POST /v1/requests {id} -> {s}"), &v);
    assert_eq!(s, 200);
    let (s2, again) = n
        .call(reqwest::Method::POST, "/v1/requests", Some(request(&id)))
        .await;
    println!(
        "--- same request again -> {s2}: taskId {} (first {})",
        again["taskId"], v["taskId"]
    );
    assert_eq!(again["taskId"], v["taskId"]);
    let settled = n.until_settled(&id, settle_limit()).await;
    say("settled", &settled);
    assert_productive(&id, &settled);
    let mut kinds = Vec::new();
    while let Ok(Some(Ok(m))) = tokio::time::timeout(Duration::from_millis(500), ws.next()).await {
        if let Message::Text(t) = m {
            let e: Value = serde_json::from_str(t.as_str()).unwrap();
            if e["requestId"] == id.as_str() || e["type"] != "request" {
                kinds.push(format!(
                    "{}#{}",
                    e["type"].as_str().unwrap_or("?"),
                    e["cursor"]
                ));
            }
        }
    }
    println!("--- events seen on the ticketed WebSocket for {id}: {kinds:?}");
    println!(
        "    (requests known before: {})",
        list["requests"].as_array().map(Vec::len).unwrap_or(0)
    );

    // 4. cancel while the task is in flight.
    let cid = format!("cs_0{:025}", stamp() * 10 + 2);
    let (_, v) = n
        .call(reqwest::Method::POST, "/v1/requests", Some(request(&cid)))
        .await;
    println!(
        "--- POST /v1/requests {cid}: state {} task {}",
        v["state"], v["taskId"]
    );
    let (s, c) = n
        .call(
            reqwest::Method::POST,
            &format!("/v1/requests/{cid}/cancel"),
            Some(json!({})),
        )
        .await;
    say(&format!("POST /v1/requests/{cid}/cancel -> {s}"), &c);
    assert_eq!(s, 200);
    assert_eq!(
        c["state"], "cancelled",
        "cancel must settle `cancelled`, not a refusal at open: {c}"
    );

    // 5. voice through a ticketed WebSocket.
    let mut vws = n.ticketed_ws("/v1/voice", "").await;
    let first = tokio::time::timeout(Duration::from_secs(30), vws.next())
        .await
        .unwrap()
        .unwrap()
        .unwrap();
    let first: Value = serde_json::from_str(first.to_text().unwrap()).unwrap();
    say("WS /v1/voice first frame", &first);
    if first["type"] == "ready" {
        let silence = base64::engine::general_purpose::STANDARD.encode(vec![0u8; 9600]);
        vws.send(Message::Text(
            json!({"type": "audio", "seq": 0, "pcm16": silence})
                .to_string()
                .into(),
        ))
        .await
        .unwrap();
        vws.send(Message::Text(json!({"type": "stop"}).to_string().into()))
            .await
            .unwrap();
    }
    while let Ok(Some(Ok(m))) = tokio::time::timeout(Duration::from_secs(10), vws.next()).await {
        if let Ok(t) = m.to_text() {
            if t.is_empty() {
                continue;
            }
            let v: Value = serde_json::from_str(t).unwrap_or(json!(t));
            println!("    voice: {v}");
            if v["type"] == "closed" {
                break;
            }
        }
    }

    // 6. media op (refusal pass-through unless allowed).
    if std::env::var("STUDIO_REAL_ALLOW_OPS").as_deref() == Ok("1") {
        let (s, v) = n
            .call(reqwest::Method::POST, "/v1/ops/generate",
                  Some(json!({"op": "image", "spec": {"prompt": "a brass lantern, game icon"}, "max_cost_usd": 0.05, "changeSetId": id})))
            .await;
        say(&format!("POST /v1/ops/generate image -> {s}"), &v);
    } else {
        let (s, v) = n
            .call(
                reqwest::Method::POST,
                "/v1/ops/generate",
                Some(json!({"op": "3d", "spec": {"prompt": "lantern"}, "max_cost_usd": 0.5})),
            )
            .await;
        say(&format!("POST /v1/ops/generate 3d -> {s}"), &v);
    }

    // 7. index delta -> Resource Graph.
    let (s, v) = n
        .call(reqwest::Method::POST, "/v1/index/delta", Some(json!({
            "project": "p05-real", "revision": 1,
            "nodes": [{"ref": {"kind": "Entity", "authoringId": "e-ferryman"}, "type": "npc.definition", "name": "Ferryman"}]
        })))
        .await;
    say(&format!("POST /v1/index/delta -> {s}"), &v);
    tokio::time::sleep(Duration::from_secs(3)).await;
    let q = n
        .http
        .post(format!("{}/api/v1/query", n.url))
        .bearer_auth(&n.key)
        .json(&json!({"sql": "SELECT authoring_id, name FROM gc_entity"}))
        .send()
        .await
        .unwrap();
    let status = q.status().as_u16();
    let body: Value = q.json().await.unwrap_or(Value::Null);
    say(
        &format!("POST /api/v1/query gc_entity (app key) -> {status}"),
        &body,
    );
}

#[tokio::test]
#[ignore = "needs a real etosd and STUDIO_REAL_ETOS (restarts the agent)"]
async fn real_node_restart_recovery() {
    let n = node().expect("STUDIO_REAL_APP_KEY");
    let etos = std::env::var("STUDIO_REAL_ETOS").expect("STUDIO_REAL_ETOS");
    let id = format!("cs_0{:025}", stamp() * 10 + 3);
    let (s, v) = n
        .call(reqwest::Method::POST, "/v1/requests", Some(request(&id)))
        .await;
    println!(
        "--- POST /v1/requests {id} -> {s}: state {} task {}",
        v["state"], v["taskId"]
    );
    let first_task = v["taskId"].clone();
    let out = std::process::Command::new(&etos)
        .args(["agent", "restart", AGENT])
        .output()
        .unwrap();
    println!(
        "--- etos agent restart {AGENT}: {}",
        String::from_utf8_lossy(&out.stdout).trim()
    );
    // The proxy answers agent_starting until the new process is ready.
    let start = Instant::now();
    loop {
        let (s, v) = n.call(reqwest::Method::GET, "/v1/hello", None).await;
        if s == 200 {
            println!(
                "  [{:>4.1}s] new process ready",
                start.elapsed().as_secs_f32()
            );
            break;
        }
        println!(
            "  [{:>4.1}s] hello -> {s} {}",
            start.elapsed().as_secs_f32(),
            v["code"]
        );
        assert!(start.elapsed() < Duration::from_secs(60));
        tokio::time::sleep(Duration::from_millis(300)).await;
    }
    let settled = n.until_settled(&id, settle_limit()).await;
    say("settled after the restart", &settled);
    assert_productive(&id, &settled);
    assert_eq!(
        settled["tasks"][0], first_task,
        "the first task was kept, never reopened"
    );
}
