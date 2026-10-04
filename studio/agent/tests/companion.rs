#![allow(clippy::unwrap_used, clippy::expect_used)]
//! The companion against a fake node (`tests/support`): request → task → candidate with
//! digest verification (success, failure, re-ask), cancel, restart recovery, voice, proxy
//! token checks and rotation, idempotency, ops, index delivery and the stage shell.

mod support;

use std::path::Path;
use std::time::Duration;

use base64::Engine as _;
use etos_sdk::{AgentOptions, Client};
use futures::{SinkExt, StreamExt};
use gamecore_studio::app::{self, Running};
use gamecore_studio::config::Config;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use support::{AGENT, AGENT_KEY, APP_KEY, FakeNode};
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;

fn sha(b: &[u8]) -> String {
    hex::encode(Sha256::digest(b))
}

async fn companion(node: &FakeNode, dir: &Path, tweak: impl FnOnce(&mut Config)) -> Running {
    let mut cfg = Config::defaults(AGENT, dir);
    cfg.follow_wait_ms = 200;
    cfg.index_flush_ms = 100;
    cfg.hello_cache_s = 0;
    tweak(&mut cfg);
    let client = Client::new(&node.url, AGENT_KEY)
        .unwrap()
        .with_agent(AGENT)
        .with_backoff(Duration::from_millis(10));
    let opts = AgentOptions {
        reconnect_min: Duration::from_millis(20),
        reconnect_max: Duration::from_millis(100),
        ..AgentOptions::default()
    };
    let running = app::start(cfg, client, opts).await.unwrap();
    let agent = running.state.agent.clone();
    tokio::spawn(async move { agent.serve().await });
    running
}

/// A direct client of the companion's listener, as the node's proxy would call it.
#[derive(Clone)]
struct Api {
    url: String,
    token: String,
    app: String,
    http: reqwest::Client,
}

impl Api {
    fn new(running: &Running, node: &FakeNode) -> Api {
        Api {
            url: running.url.clone(),
            token: node.token(),
            app: "gamecore-unity".into(),
            http: reqwest::Client::new(),
        }
    }

    fn as_app(&self, app: &str) -> Api {
        let mut a = self.clone();
        a.app = app.into();
        a
    }

    async fn raw(
        &self,
        method: reqwest::Method,
        path: &str,
        body: Vec<u8>,
    ) -> (u16, String, Value) {
        let res = self
            .http
            .request(method, format!("{}{path}", self.url))
            .header("x-etos-proxy-token", &self.token)
            .header("x-etos-app", &self.app)
            .header("content-type", "application/json")
            .body(body)
            .send()
            .await
            .unwrap();
        let status = res.status().as_u16();
        let ct = res
            .headers()
            .get("content-type")
            .and_then(|v| v.to_str().ok())
            .unwrap_or_default()
            .to_string();
        let bytes = res.bytes().await.unwrap();
        (
            status,
            ct,
            serde_json::from_slice(&bytes).unwrap_or(Value::Null),
        )
    }

    async fn call(&self, method: reqwest::Method, path: &str, body: Option<Value>) -> (u16, Value) {
        let mut req = self
            .http
            .request(method, format!("{}{path}", self.url))
            .header("x-etos-proxy-token", &self.token)
            .header("x-etos-app", &self.app);
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

    async fn get(&self, path: &str) -> (u16, Value) {
        self.call(reqwest::Method::GET, path, None).await
    }

    async fn post(&self, path: &str, body: Value) -> (u16, Value) {
        self.call(reqwest::Method::POST, path, Some(body)).await
    }

    async fn until_state(&self, id: &str, state: &str) -> Value {
        for _ in 0..500 {
            let (s, v) = self.get(&format!("/v1/requests/{id}")).await;
            assert_eq!(s, 200, "{v}");
            if v["state"] == state {
                return v;
            }
            tokio::time::sleep(Duration::from_millis(20)).await;
        }
        let (_, v) = self.get(&format!("/v1/requests/{id}")).await;
        panic!("request {id} never reached {state}: {v}");
    }

    async fn ws(&self, path: &str) -> Ws {
        let url = format!("{}{path}", self.url.replacen("http", "ws", 1));
        let mut req = url.into_client_request().unwrap();
        req.headers_mut()
            .insert("x-etos-proxy-token", self.token.parse().unwrap());
        req.headers_mut()
            .insert("x-etos-app", "gamecore-unity".parse().unwrap());
        let (ws, _) = tokio_tungstenite::connect_async(req).await.unwrap();
        ws
    }
}

type Ws =
    tokio_tungstenite::WebSocketStream<tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>>;

async fn next_json(ws: &mut Ws) -> Value {
    let m = tokio::time::timeout(Duration::from_secs(5), ws.next())
        .await
        .unwrap()
        .unwrap()
        .unwrap();
    serde_json::from_str::<Value>(m.to_text().unwrap()).unwrap()
}

/// The tool catalog of the fixtures (`tool-catalog.schema.json`).
fn catalog() -> Value {
    let mut c = bare_catalog();
    let revision = gamecore_studio::util::catalog_revision(&c);
    c["revision"] = json!(revision);
    c
}

fn bare_catalog() -> Value {
    json!({"schema": "gamecore.studio.toolcatalog/1", "objectTypes": [], "tools": [
        {"id": "inventory.grantStarting", "tier": "Configure", "runtimeApply": "Live", "targetRequired": true,
         "targetKinds": ["Entity"],
         "args": [{"name": "item", "type": "ref", "required": true}, {"name": "count", "type": "int", "required": false}]},
        {"id": "dialogue.addNode", "tier": "Compose", "runtimeApply": "Live", "targetRequired": true,
         "args": [{"name": "text", "type": "string", "required": true}, {"name": "voice", "type": "artifact", "required": false}]}
    ]})
}

fn catalog_rev() -> String {
    gamecore_studio::util::catalog_revision(&catalog())
}

fn edit_request(id: &str) -> Value {
    json!({
        "changeSetId": id,
        "intent": {"text": "Give the ferryman a lantern and make him mention it", "origin": "agent"},
        "selection": {"id": "sel_01J9ZQ00000000000000000001", "mode": "Edit", "indexRevision": 12,
                      "targets": [{"kind": "Entity", "authoringId": "e-ferryman", "stamp": format!("sha256:{}", "a".repeat(64))}]},
        "contextSlice": {"revision": 12, "project": "hollowmere",
                         "nodes": [{"ref": {"kind": "Entity", "authoringId": "e-ferryman"}, "type": "npc.definition",
                                    "name": "Ferryman", "rgKind": "gc_entity"}],
                         "edges": []},
        "toolCatalogRevision": catalog_rev(),
        "toolCatalog": catalog(),
        "attachments": [{"name": "frame.png", "mediaType": "image/png", "role": "frame",
                         "data": base64::engine::general_purpose::STANDARD.encode(b"\x89PNG frame")}]
    })
}

fn changeset(id: &str, artifact: &[u8], claimed_sha: Option<&str>) -> Vec<u8> {
    let h = claimed_sha
        .map(str::to_string)
        .unwrap_or_else(|| sha(artifact));
    serde_json::to_vec(&json!({
        "id": id, "schema": "gamecore.studio.changeset/1",
        "intent": {"text": "Give the ferryman a lantern", "origin": "agent"},
        "selection": {"id": "sel_01J9ZQ00000000000000000001", "mode": "Edit", "indexRevision": 12, "targets": []},
        "operations": [
            {"opId": "op1", "tool": "inventory.grantStarting", "target": {"kind": "Entity", "authoringId": "e-ferryman"},
             "args": {"item": "item.lantern@2", "count": 1}, "dependsOn": [], "preconditions": "stamp", "applyRequirement": "Live"},
            {"opId": "op2", "tool": "dialogue.addNode", "target": {"kind": "Entity", "authoringId": "e-ferryman"},
             "args": {"text": "Mind the lantern.", "voice": {"artifact": format!("sha256:{h}")}}, "dependsOn": ["op1"]}
        ],
        "artifacts": [{"sha256": h, "name": "ferryman_line_07.wav", "mediaType": "audio/wav", "bytes": artifact.len(),
                       "producer": {"op": "tts"}, "role": "voiceLine"}]
    }))
    .unwrap()
}

#[tokio::test]
async fn request_task_candidate_with_verified_artifacts() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);

    let (s, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000001"),
        )
        .await;
    assert_eq!(s, 200, "{v}");
    assert_eq!(v["requestId"], "cs_01J9ZQ00000000000000000001");
    assert_eq!(v["state"], "running");
    let task = v["taskId"].as_str().unwrap().to_string();
    let t = node.tasks().into_iter().find(|t| t.id == task).unwrap();
    assert_eq!(t.request, "cs_01J9ZQ00000000000000000001");
    assert_eq!(t.worker, "gc-designer");
    assert_eq!(
        t.topic,
        "#agent/gamecore-studio/cs-01j9zq00000000000000000001"
    );
    let names: Vec<String> = t.inputs.iter().map(|r| node.file(r).name).collect();
    assert_eq!(
        names,
        [
            "request.md",
            "selection.json",
            "index-slice.json",
            "tool-catalog.json",
            "frame.png"
        ]
    );
    let slice: Value = serde_json::from_slice(&node.file(&t.inputs[2]).bytes).unwrap();
    assert!(slice["nodes"][0].get("rgKind").is_none(), "{slice}");
    let md = String::from_utf8(node.file(&t.inputs[0]).bytes).unwrap();
    assert!(
        md.contains("`cs_01J9ZQ00000000000000000001`")
            && md.contains("Index revision: `12`")
            && md.contains("ferryman"),
        "{md}"
    );
    assert_eq!(node.file(&t.inputs[4]).bytes, b"\x89PNG frame");

    // Events from the start, until the candidate.
    let mut ws = api.ws("/v1/events?after=0").await;

    let wav = b"RIFF\x24\x00\x00\x00WAVEfmt ferryman".to_vec();
    node.complete(
        &task,
        &[
            (
                "changeset.json",
                changeset("cs_01J9ZQ00000000000000000001", &wav, None),
            ),
            ("ferryman_line_07.wav", wav.clone()),
            ("notes.txt", b"scratch".to_vec()),
        ],
    );
    let v = api
        .until_state("cs_01J9ZQ00000000000000000001", "candidate")
        .await;
    assert_eq!(v["taskStatus"], "done");
    assert_eq!(v["hasCandidate"], true);

    let (s, c) = api
        .get("/v1/candidates/cs_01J9ZQ00000000000000000001")
        .await;
    assert_eq!(s, 200, "{c}");
    assert_eq!(c["taskId"], task.as_str());
    assert_eq!(c["changeSet"]["operations"].as_array().unwrap().len(), 2);
    let art = &c["artifacts"][0];
    assert_eq!(art["sha256"], sha(&wav));
    assert_eq!(art["mediaType"], "audio/wav");
    assert_eq!(c["diagnostics"][0]["code"], "CandidateInvalid");
    assert!(
        c["diagnostics"][0]["message"]
            .as_str()
            .unwrap()
            .starts_with("output_unlisted: ")
    );
    let bytes = reqwest::Client::new()
        .get(format!("{}/v1/artifacts/{}", running.url, sha(&wav)))
        .header("x-etos-proxy-token", node.token())
        .header("x-etos-app", "gamecore-unity")
        .send()
        .await
        .unwrap();
    assert_eq!(bytes.headers()["content-type"], "audio/wav");
    assert_eq!(bytes.bytes().await.unwrap().to_vec(), wav);
    let stored = dir
        .path()
        .join("artifacts/sha256")
        .join(&sha(&wav)[..2])
        .join(sha(&wav));
    assert!(stored.is_file());

    let mut kinds = Vec::new();
    let mut last = 0;
    while !kinds.iter().any(|k| k == "candidate") {
        let m = tokio::time::timeout(Duration::from_secs(5), ws.next())
            .await
            .unwrap()
            .unwrap()
            .unwrap();
        if let Message::Text(t) = m {
            let e: Value = serde_json::from_str(t.as_str()).unwrap();
            assert!(e["cursor"].as_i64().unwrap() > last, "cursors increase");
            last = e["cursor"].as_i64().unwrap();
            kinds.push(e["type"].as_str().unwrap().to_string());
        }
    }
    assert!(
        kinds.contains(&"request".to_string()) && kinds.contains(&"task_progress".to_string()),
        "{kinds:?}"
    );

    // The desk's own states reach the Resource Graph binding.
    node.until("gc_changeset candidate trace", |g| {
        g.traces.iter().any(|t| {
            t["kind"] == "gc_changeset"
                && t["key"] == "cs_01J9ZQ00000000000000000001"
                && t["values"]["state"] == "candidate"
        }) && g
            .traces
            .iter()
            .any(|t| t["kind"] == "gc_asset" && t["key"] == sha(&wav))
    })
    .await;
    assert!(node.lock().binding.as_ref().unwrap()["states"]["gc_entity"].is_object());
    running.shutdown().await;
}

#[tokio::test]
async fn digest_mismatch_is_reasked_once_with_parent() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    let (_, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000002"),
        )
        .await;
    let first = v["taskId"].as_str().unwrap().to_string();
    let wav = b"RIFFxxxxWAVE".to_vec();
    // The manifest claims bytes that were not delivered.
    node.complete(
        &first,
        &[
            (
                "changeset.json",
                changeset("cs_01J9ZQ00000000000000000002", &wav, Some(&sha(b"other"))),
            ),
            ("line.wav", wav.clone()),
        ],
    );
    node.until("re-ask task", |g| g.tasks.len() == 2).await;
    let second = node.tasks()[1].clone();
    assert_eq!(second.request, "cs_01J9ZQ00000000000000000002.r1");
    assert_eq!(
        second.topic,
        "#agent/gamecore-studio/cs-01j9zq00000000000000000002-r1"
    );
    let names: Vec<String> = second.inputs.iter().map(|r| node.file(r).name).collect();
    assert_eq!(names.first().map(String::as_str), Some("request.md"));
    assert_eq!(names.last().map(String::as_str), Some("diagnostics.json"));
    let md = String::from_utf8(node.file(&second.inputs[0]).bytes).unwrap();
    assert!(md.contains(&format!("re-ask** of task `{first}`")), "{md}");
    let diags: Value =
        serde_json::from_slice(&node.file(second.inputs.last().unwrap()).bytes).unwrap();
    assert_eq!(diags["parentTask"], first.as_str());
    assert!(diags["diagnostics"].as_array().unwrap().iter().any(|d| {
        d["code"] == "CandidateInvalid"
            && d["message"]
                .as_str()
                .unwrap()
                .starts_with("artifact_digest_mismatch: ")
    }));
    let v = api
        .until_state("cs_01J9ZQ00000000000000000002", "running")
        .await;
    assert_eq!(v["attempt"], 1);

    // The re-ask delivers correctly.
    node.complete(
        &second.id,
        &[
            (
                "changeset.json",
                changeset("cs_01J9ZQ00000000000000000002", &wav, None),
            ),
            ("line.wav", wav.clone()),
        ],
    );
    let v = api
        .until_state("cs_01J9ZQ00000000000000000002", "candidate")
        .await;
    assert_eq!(v["tasks"], json!([first, second.id]));
    let (_, c) = api
        .get("/v1/candidates/cs_01J9ZQ00000000000000000002")
        .await;
    assert_eq!(c["attempt"], 1);
    assert_eq!(node.calls("POST", "/tasks"), 2);
    running.shutdown().await;
}

#[tokio::test]
async fn invalid_candidate_without_reask_is_terminal() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| c.reask_on_invalid = false).await;
    let api = Api::new(&running, &node);
    let (_, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000003"),
        )
        .await;
    let task = v["taskId"].as_str().unwrap().to_string();
    let wav = b"RIFFyyyyWAVE".to_vec();
    node.complete(
        &task,
        &[
            (
                "changeset.json",
                changeset("cs_01J9ZQ00000000000000000003", &wav, Some(&sha(b"x"))),
            ),
            ("line.wav", wav),
        ],
    );
    let v = api
        .until_state("cs_01J9ZQ00000000000000000003", "candidate_invalid")
        .await;
    assert_eq!(v["outcome"]["code"], "candidate_invalid");
    assert!(
        v["outcome"]["diagnostics"]
            .as_array()
            .unwrap()
            .iter()
            .any(|d| d["code"] == "CandidateInvalid"
                && d["message"]
                    .as_str()
                    .unwrap()
                    .starts_with("artifact_digest_mismatch: "))
    );
    let (s, c) = api
        .get("/v1/candidates/cs_01J9ZQ00000000000000000003")
        .await;
    assert_eq!((s, c["code"].as_str()), (404, Some("not_found")));
    assert_eq!(node.tasks().len(), 1);
    running.shutdown().await;
}

#[tokio::test]
async fn cancel_reports_the_etos_status() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    let (_, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000004"),
        )
        .await;
    let task = v["taskId"].as_str().unwrap().to_string();
    let (s, v) = api
        .post(
            "/v1/requests/cs_01J9ZQ00000000000000000004/cancel",
            json!({}),
        )
        .await;
    assert_eq!(s, 200, "{v}");
    assert_eq!(v["state"], "cancelled");
    assert_eq!(v["taskStatus"], "cancelled");
    assert_eq!(node.calls("POST", &format!("/tasks/{task}/cancel")), 1);
    // The node then closes the task with a final `failed` record: the request stays cancelled.
    let topic = node.tasks()[0].topic.clone();
    node.until("the closing record", |g| {
        g.topics.get(&topic).is_some_and(|r| r.len() >= 2)
    })
    .await;
    tokio::time::sleep(Duration::from_millis(400)).await;
    let (_, v) = api.get("/v1/requests/cs_01J9ZQ00000000000000000004").await;
    assert_eq!(
        (v["state"].as_str(), v["taskStatus"].as_str()),
        (Some("cancelled"), Some("cancelled")),
        "{v}"
    );
    // Cancelling again is a no-op answered from the ledger.
    let (s, v) = api
        .post(
            "/v1/requests/cs_01J9ZQ00000000000000000004/cancel",
            json!({}),
        )
        .await;
    assert_eq!((s, v["state"].as_str()), (200, Some("cancelled")));
    assert_eq!(node.calls("POST", &format!("/tasks/{task}/cancel")), 1);
    let (s, v) = api.post("/v1/requests/nope/cancel", json!({})).await;
    assert_eq!((s, v["code"].as_str()), (404, Some("not_found")));
    running.shutdown().await;
}

#[tokio::test]
async fn restart_resumes_from_the_ledger_without_reopening() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let a = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&a, &node);
    let (_, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000005"),
        )
        .await;
    let task = v["taskId"].as_str().unwrap().to_string();
    let topic = node.tasks()[0].topic.clone();
    // A progress record seen by the first process advances its cursor.
    node.post_record(
        &topic,
        "gc-designer@fake",
        "reading the catalog",
        Some("working"),
        vec![],
    );
    node.until("cursor past the progress record", |g| {
        g.topic_reads
            .iter()
            .any(|(t, after)| t == &topic && *after >= 2)
    })
    .await;
    a.shutdown().await;

    // The worker finishes while the companion is down.
    let wav = b"RIFFzzzzWAVE".to_vec();
    node.complete(
        &task,
        &[
            (
                "changeset.json",
                changeset("cs_01J9ZQ00000000000000000005", &wav, None),
            ),
            ("l.wav", wav),
        ],
    );
    let reads_before = node.lock().topic_reads.len();

    let b = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&b, &node);
    let v = api
        .until_state("cs_01J9ZQ00000000000000000005", "candidate")
        .await;
    assert_eq!(v["taskId"], task.as_str());
    assert_eq!(
        node.calls("POST", "/tasks"),
        1,
        "the task is never reopened"
    );
    let resumed_from = node.lock().topic_reads[reads_before..]
        .iter()
        .find(|(t, _)| t == &topic)
        .map(|(_, a)| *a)
        .unwrap();
    assert!(
        resumed_from >= 2,
        "resumed from the saved cursor, not from 0 ({resumed_from})"
    );
    // The same request again is answered from the ledger.
    let (s, v) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000005"),
        )
        .await;
    assert_eq!((s, v["taskId"].as_str()), (200, Some(task.as_str())));
    assert_eq!(node.calls("POST", "/tasks"), 1);
    b.shutdown().await;
}

#[tokio::test]
async fn voice_session_ready_transcripts_and_close() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    let mut ws = api.ws("/v1/voice").await;
    let ready = next_json(&mut ws).await;
    assert_eq!(ready["type"], "ready", "{ready}");
    assert_eq!(ready["sampleRateHz"], 24000);
    let session = ready["sessionId"].as_str().unwrap().to_string();
    // A frame over 24 KiB raw is refused and not forwarded.
    let big = base64::engine::general_purpose::STANDARD.encode(vec![0u8; 24 * 1024 + 2]);
    ws.send(Message::Text(
        json!({"type": "audio", "seq": 0, "pcm16": big})
            .to_string()
            .into(),
    ))
    .await
    .unwrap();
    let refused = next_json(&mut ws).await;
    assert_eq!(
        (refused["type"].as_str(), refused["code"].as_str()),
        (Some("error"), Some("too_large")),
        "{refused}"
    );
    // Three frames of at most 24 KiB → three etos chunks.
    for (i, n) in [24 * 1024, 24 * 1024, 60_000 - 48 * 1024]
        .into_iter()
        .enumerate()
    {
        let b64 = base64::engine::general_purpose::STANDARD.encode(vec![0u8; n]);
        ws.send(Message::Text(
            json!({"type": "audio", "seq": i + 1, "pcm16": b64})
                .to_string()
                .into(),
        ))
        .await
        .unwrap();
    }
    let started = next_json(&mut ws).await;
    assert_eq!(started["type"], "speech_started");
    let partial = next_json(&mut ws).await;
    assert_eq!(partial["type"], "transcript", "{partial}");
    assert_eq!(partial["role"], "user");
    assert!(
        partial.get("final").is_none() && partial.get("done").is_none(),
        "{partial}"
    );
    assert!(
        partial.get("responseId").is_none(),
        "no null members: {partial}"
    );
    ws.send(Message::Text(json!({"type": "stop"}).to_string().into()))
        .await
        .unwrap();
    let mut seen = Vec::new();
    loop {
        let m = next_json(&mut ws).await;
        seen.push(m.clone());
        if m["type"] == "closed" {
            break;
        }
    }
    let transcripts: Vec<&Value> = seen.iter().filter(|m| m["type"] == "transcript").collect();
    assert_eq!(
        transcripts.len(),
        1,
        "assistant transcripts are dropped: {seen:?}"
    );
    let done = transcripts[0];
    assert_eq!(done["text"], "give the ferryman a lantern");
    assert_eq!(done["final"], true);
    assert_eq!(done["revision"], 1);
    assert!(seen.iter().any(|m| m["type"] == "speech_ended"));
    {
        let g = node.lock();
        assert_eq!(g.rt_chunks, vec![24 * 1024, 24 * 1024, 60_000 - 48 * 1024]);
        assert!(g.rt_errors.is_empty(), "{:?}", g.rt_errors);
        assert!(
            g.rt_actions
                .iter()
                .all(|a| a == "input_audio" || a == "close"),
            "transcription only: {:?}",
            g.rt_actions
        );
    }
    // The session is in the ledger, ended, with its counters; the final transcript is an event.
    for _ in 0..100 {
        if let Ok((Some(_), _, 3, 1)) = running.state.ledger.voice_session(&session) {
            break;
        }
        tokio::time::sleep(Duration::from_millis(20)).await;
    }
    let (ended, _, chunks, transcripts) = running.state.ledger.voice_session(&session).unwrap();
    assert!(ended.is_some());
    assert_eq!((chunks, transcripts), (3, 1));
    let events = running.state.ledger.events_after(0, 100).unwrap();
    assert!(
        events.iter().any(
            |e| e.kind == "voice_transcript" && e.data["text"] == "give the ferryman a lantern"
        )
    );
    let (_, hello) = api.get("/v1/hello").await;
    assert_eq!(hello["providers"]["voice"], "live");
    running.shutdown().await;
}

#[tokio::test]
async fn voice_refusal_passes_through() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| {
        c.voice.provider = "missing-voice".into()
    })
    .await;
    let api = Api::new(&running, &node);
    let mut ws = api.ws("/v1/voice").await;
    let m = tokio::time::timeout(Duration::from_secs(5), ws.next())
        .await
        .unwrap()
        .unwrap()
        .unwrap();
    let v: Value = serde_json::from_str(m.to_text().unwrap()).unwrap();
    assert_eq!(
        (v["type"].as_str(), v["code"].as_str()),
        (Some("error"), Some("not_configured")),
        "{v}"
    );
    let (_, hello) = api.get("/v1/hello").await;
    assert_eq!(hello["providers"]["voice"], "not_configured");
    running.shutdown().await;

    // Refused at admission with an HTTP answer (the SDK keeps only the status): the code is
    // still the node's.
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| {
        c.voice.provider = "http-refused".into()
    })
    .await;
    let api = Api::new(&running, &node);
    let mut ws = api.ws("/v1/voice").await;
    let v = next_json(&mut ws).await;
    assert_eq!(
        (v["type"].as_str(), v["code"].as_str()),
        (Some("error"), Some("not_configured")),
        "{v}"
    );
    let v = next_json(&mut ws).await;
    assert_eq!(v["type"], "closed");
    running.shutdown().await;
}

#[tokio::test]
async fn proxy_token_and_app_are_checked_and_follow_restarts() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let http = reqwest::Client::new();
    let hello = |token: &str, app: &str| {
        http.get(format!("{}/v1/hello", running.url))
            .header("x-etos-proxy-token", token)
            .header("x-etos-app", app)
            .send()
    };
    let r = hello("etp_wrong_token_0000001", "gamecore-unity")
        .await
        .unwrap();
    assert_eq!(r.status(), 403);
    let body: Value = r.json().await.unwrap();
    assert_eq!(body["code"], "forbidden");
    assert!(body["hint"].is_string());
    let r = http
        .get(format!("{}/v1/hello", running.url))
        .send()
        .await
        .unwrap();
    assert_eq!(r.status(), 403, "no token");
    let r = hello(&node.token(), "some-other-app").await.unwrap();
    assert_eq!(r.status(), 403, "app not allowed");
    let r = hello(&node.token(), "gamecore-unity").await.unwrap();
    assert_eq!(r.status(), 200);
    let v: Value = r.json().await.unwrap();
    assert_eq!(v["service"], AGENT);
    assert_eq!(v["providers"]["image"], "live");
    assert_eq!(v["providers"]["tts"], "not_configured");
    assert_eq!(v["providers"]["3d"], "not_configured");
    assert_eq!(v["providers"]["voice"], "unknown");

    // Through the node's proxy with the app key (Authorization stripped, headers added).
    let r = http
        .get(format!("{}/api/v1/agents/{AGENT}/http/v1/hello", node.url))
        .header("authorization", format!("Bearer {APP_KEY}"))
        .send()
        .await
        .unwrap();
    assert_eq!(r.status(), 200);
    let v: Value = r.json().await.unwrap();
    assert_eq!(v["app"], "gamecore-unity");
    assert_eq!(v["node"], "fake");
    let t = http
        .post(format!("{}/api/v1/tickets", node.url))
        .header("authorization", format!("Bearer {APP_KEY}"))
        .json(&json!({"path": format!("/api/v1/agents/{AGENT}/http/v1/events")}))
        .send()
        .await
        .unwrap();
    assert_eq!(t.status(), 200);

    // etosd restarts: a new token; the companion reconnects, re-registers and follows it.
    let old = node.token();
    let puts = node.lock().endpoint_puts;
    node.restart("etp_second_token_00002");
    node.until("re-registration", |g| {
        g.endpoint_puts > puts && g.hellos >= 2
    })
    .await;
    for _ in 0..100 {
        if hello("etp_second_token_00002", "gamecore-unity")
            .await
            .unwrap()
            .status()
            == 200
        {
            break;
        }
        tokio::time::sleep(Duration::from_millis(20)).await;
    }
    assert_eq!(
        hello("etp_second_token_00002", "gamecore-unity")
            .await
            .unwrap()
            .status(),
        200
    );
    assert_eq!(
        hello(&old, "gamecore-unity").await.unwrap().status(),
        403,
        "the old token is refused"
    );
    running.shutdown().await;
}

#[tokio::test]
async fn requests_are_idempotent_and_conflicts_refused() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    let (s1, a) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000006"),
        )
        .await;
    let (s2, b) = api
        .post(
            "/v1/requests",
            edit_request("cs_01J9ZQ00000000000000000006"),
        )
        .await;
    assert_eq!((s1, s2), (200, 200));
    assert_eq!(a["taskId"], b["taskId"]);
    assert_eq!(node.calls("POST", "/tasks"), 1);
    let mut other = edit_request("cs_01J9ZQ00000000000000000006");
    other["intent"]["text"] = json!("something else");
    let (s, v) = api.post("/v1/requests", other).await;
    assert_eq!(
        (s, v["code"].as_str()),
        (409, Some("ledger_conflict")),
        "{v}"
    );
    // A catalog revision the companion does not hold.
    let mut stale = edit_request("cs_01J9ZQ00000000000000000007");
    stale["toolCatalogRevision"] = json!("0".repeat(64));
    stale.as_object_mut().unwrap().remove("toolCatalog");
    let (s, v) = api.post("/v1/requests", stale).await;
    assert_eq!((s, v["code"].as_str()), (409, Some("stale_context")), "{v}");
    // A held revision needs no catalog.
    let mut held = edit_request("cs_01J9ZQ00000000000000000008");
    held.as_object_mut().unwrap().remove("toolCatalog");
    let (s, v) = api.post("/v1/requests", held).await;
    assert_eq!(s, 200, "{v}");
    // The idempotency digest leaves out `toolCatalog` and attachment encoding: the same
    // request without its catalog, and with the attachment's digest spelled out, is the same.
    let mut again = edit_request("cs_01J9ZQ00000000000000000006");
    again.as_object_mut().unwrap().remove("toolCatalog");
    again["attachments"][0]["sha256"] = json!(sha(b"\x89PNG frame"));
    let (s, v) = api.post("/v1/requests", again).await;
    assert_eq!((s, &v["taskId"]), (200, &a["taskId"]), "{v}");
    // A catalog whose digest is not the revision, a selection or slice off the schema, a
    // null: refused with findings.
    let mut wrong = edit_request("cs_01J9ZQ00000000000000000013");
    wrong["toolCatalogRevision"] = json!("1".repeat(64));
    let (s, v) = api.post("/v1/requests", wrong).await;
    assert_eq!((s, v["code"].as_str()), (400, Some("bad_request")), "{v}");
    let mut bad_sel = edit_request("cs_01J9ZQ00000000000000000014");
    bad_sel["selection"]["id"] = json!("sel_1");
    bad_sel["contextSlice"]
        .as_object_mut()
        .unwrap()
        .remove("project");
    let (s, v) = api.post("/v1/requests", bad_sel).await;
    assert_eq!((s, v["code"].as_str()), (400, Some("bad_request")), "{v}");
    let findings: Vec<&str> = v["diagnostics"]
        .as_array()
        .unwrap()
        .iter()
        .map(|d| d["message"].as_str().unwrap())
        .collect();
    assert!(
        findings.iter().any(|m| m.starts_with("selection: ")),
        "{findings:?}"
    );
    assert!(
        findings.iter().any(|m| m.starts_with("contextSlice: ")),
        "{findings:?}"
    );
    let mut null = edit_request("cs_01J9ZQ00000000000000000015");
    null["worker"] = Value::Null;
    let (s, v) = api.post("/v1/requests", null).await;
    assert_eq!((s, v["code"].as_str()), (400, Some("bad_request")), "{v}");
    assert!(v["message"].as_str().unwrap().contains("/worker"), "{v}");
    // An etos refusal passes through and the request is failed.
    let mut refused = edit_request("cs_01J9ZQ00000000000000000009");
    refused["worker"] = json!("nobody");
    let (s, v) = api.post("/v1/requests", refused).await;
    assert_eq!(
        (s, v["code"].as_str()),
        (400, Some("bad_request")),
        "not a Studio worker: {v}"
    );
    let (s, v) = api
        .post(
            "/v1/requests",
            json!({"changeSetId": "bad id", "intent": {"text": "x"}, "toolCatalogRevision": 1}),
        )
        .await;
    assert_eq!((s, v["code"].as_str()), (400, Some("bad_request")));
    // Listing after a cursor.
    let (_, list) = api.get("/v1/requests?after=0").await;
    let ids: Vec<&str> = list["requests"]
        .as_array()
        .unwrap()
        .iter()
        .map(|r| r["requestId"].as_str().unwrap())
        .collect();
    assert!(
        ids.contains(&"cs_01J9ZQ00000000000000000006")
            && ids.contains(&"cs_01J9ZQ00000000000000000008")
    );
    let (_, empty) = api
        .get(&format!("/v1/requests?after={}", list["next"]))
        .await;
    assert!(empty["requests"].as_array().unwrap().is_empty());
    running.shutdown().await;
}

#[tokio::test]
async fn etos_task_refusal_passes_through() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| c.workers.push("nobody".into())).await;
    let api = Api::new(&running, &node);
    let mut req = edit_request("cs_01J9ZQ00000000000000000010");
    req["worker"] = json!("nobody");
    let (s, v) = api.post("/v1/requests", req).await;
    assert_eq!((s, v["code"].as_str()), (403, Some("not_yours")), "{v}");
    let (_, v) = api.get("/v1/requests/cs_01J9ZQ00000000000000000010").await;
    assert_eq!(v["state"], "failed");
    assert_eq!(v["outcome"]["code"], "task_failed");
    assert_eq!(v["outcome"]["refusal"]["code"], "not_yours");
    assert_eq!(v["outcome"]["refusal"]["status"], 403);
    running.shutdown().await;
}

#[tokio::test]
async fn ops_generate_stores_artifacts_and_passes_refusals() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    node.set_op(
        "generate.image",
        200,
        json!({"name": "lantern.png", "bytes": "PNG-lantern"}),
    );
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "image", "spec": {"prompt": "a brass lantern", "size": "512x512"},
                                          "max_cost_usd": 0.05, "changeSetId": "cs_01J9ZQ00000000000000000011"}),
        )
        .await;
    assert_eq!(s, 200, "{v}");
    assert_eq!(v["etosOp"], "generate.image");
    assert_eq!(v["artifacts"][0]["sha256"], sha(b"PNG-lantern"));
    assert_eq!(v["artifacts"][0]["mediaType"], "image/png");
    let (op, body) = node
        .lock()
        .op_calls
        .iter()
        .find(|(o, _)| o == "generate.image")
        .cloned()
        .unwrap();
    assert_eq!(op, "generate.image");
    assert_eq!(body["max_cost_usd"], 0.05);
    assert!(body["key"].as_str().unwrap().starts_with("gc-"));
    assert!(body.get("output").is_none());
    assert_eq!(
        node.calls("POST", "/ops/generate"),
        0,
        "never the SDK's wrong path"
    );
    // Same spec → same etops key (a lost answer is looked up, not generated twice).
    api.post(
        "/v1/ops/generate",
        json!({"op": "image", "spec": {"prompt": "a brass lantern", "size": "512x512"},
                                         "max_cost_usd": 0.05, "changeSetId": "cs_01J9ZQ00000000000000000011"}),
    )
    .await;
    let keys: Vec<Value> = node
        .lock()
        .op_calls
        .iter()
        .filter(|(o, _)| o == "generate.image")
        .map(|(_, b)| b["key"].clone())
        .collect();
    assert_eq!(keys[0], keys[1]);
    // tts is not configured on this node: the refusal passes through.
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "tts", "spec": {"text": "Mind the lantern."}, "max_cost_usd": 0.01}),
        )
        .await;
    assert_eq!(
        (s, v["code"].as_str()),
        (404, Some("not_configured")),
        "{v}"
    );
    // A cost ceiling is required (none is configured here).
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "tts", "spec": {"text": "Mind the lantern."}}),
        )
        .await;
    assert_eq!((s, v["code"].as_str()), (400, Some("bad_request")), "{v}");
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "3d", "spec": {"prompt": "x"}, "max_cost_usd": 0.5}),
        )
        .await;
    assert_eq!((s, v["code"].as_str()), (404, Some("not_configured")));
    let (s, _) = api
        .post("/v1/ops/generate", json!({"op": "video", "spec": {}}))
        .await;
    assert_eq!(s, 400);
    // describe a stored artifact: uploaded as a reference, text answered.
    node.set_op(
        "describe",
        200,
        json!({"provider": "echo-chat", "text": "a brass lantern", "usage": {}}),
    );
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "describe", "spec": {"artifact": sha(b"PNG-lantern")}, "max_cost_usd": 0.02}),
        )
        .await;
    assert_eq!(s, 200, "{v}");
    assert_eq!(v["text"], "a brass lantern");
    let (_, b) = node
        .lock()
        .op_calls
        .iter()
        .find(|(o, _)| o == "describe")
        .cloned()
        .unwrap();
    assert!(b["input"].as_str().unwrap().starts_with("ref_"));
    assert_eq!(
        b["max_cost_usd"], 0.02,
        "every operation carries the ceiling"
    );
    // The node's reported digest is checked: a mismatch is a protocol error, nothing stored.
    node.lock().wrong_digest_ops.push("generate.image".into());
    let (s, v) = api
        .post(
            "/v1/ops/generate",
            json!({"op": "image", "spec": {"prompt": "another lantern"}, "max_cost_usd": 0.05}),
        )
        .await;
    assert_eq!((s, v["code"].as_str()), (502, Some("protocol")), "{v}");
    running.shutdown().await;
}

#[tokio::test]
async fn index_deltas_are_coalesced_and_delivered() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| c.index_flush_ms = 300).await;
    let api = Api::new(&running, &node);
    node.until("binding", |g| g.binding.is_some()).await;
    let posts_before = node.lock().trace_posts;
    for rev in 1..=5u64 {
        let (s, v) = api
            .post("/v1/index/delta", json!({
                "project": "hollowmere", "revision": rev, "projectInfo": {"unity": "6000.0.75f1"},
                "nodes": [
                    {"ref": {"kind": "Entity", "authoringId": "e-ferryman", "definition": "npc.ferryman@3"}, "type": "npc.definition",
                     "name": format!("Ferryman v{rev}"), "fields": {"speed": {"value": 1.8, "unit": "m/s", "type": "float"}},
                     "refs": [{"field": "region", "to": {"kind": "Region", "authoringId": "marsh"}}]},
                    {"ref": {"kind": "SceneObject", "authoringId": "rock"}, "type": "Rock"}
                ],
                "removals": [{"kind": "Entity", "authoringId": "e-old"}]
            }))
            .await;
        assert_eq!(s, 200, "{v}");
        assert_eq!(
            (v["queued"].as_u64(), v["skipped"].as_u64()),
            (Some(3), Some(1))
        );
    }
    node.until("entity trace", |g| {
        g.traces
            .iter()
            .any(|t| t["kind"] == "gc_entity" && t["key"] == "e-ferryman")
    })
    .await;
    {
        let g = node.lock();
        let ferry: Vec<&Value> = g
            .traces
            .iter()
            .filter(|t| t["kind"] == "gc_entity" && t["key"] == "e-ferryman")
            .collect();
        assert_eq!(
            ferry.len(),
            1,
            "five deltas within one window coalesce into one row"
        );
        assert_eq!(ferry[0]["values"]["name"], "Ferryman v5");
        assert_eq!(ferry[0]["values"]["region"], "marsh");
        assert!(ferry[0]["user"].is_null(), "global state");
        assert!(g.traces.iter().any(|t| t["kind"] == "gc_entity"
            && t["key"] == "e-old"
            && t["values"]["removed"] == true));
        assert!(
            g.traces
                .iter()
                .any(|t| t["kind"] == "gc_project" && t["values"]["index_revision"] == 5)
        );
        assert!(
            g.trace_posts - posts_before <= 2,
            "at most one batch per window ({})",
            g.trace_posts - posts_before
        );
    }
    let (_, hello) = api.get("/v1/hello").await;
    assert_eq!(hello["indexRevision"], 5);
    running.shutdown().await;
}

#[tokio::test]
async fn stage_shell_runs_the_command_or_reports_stage_failed() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    // No command configured.
    let running = companion(&node, dir.path(), |c| c.stage.command = None).await;
    let api = Api::new(&running, &node);
    let (s, v) = api
        .post(
            "/v1/stage",
            json!({"changeSetId": "cs_01J9ZQ00000000000000000012", "packageRef": "0".repeat(64)}),
        )
        .await;
    assert_eq!((s, v["code"].as_str()), (503, Some("stage_failed")), "{v}");
    assert!(v["hint"].as_str().unwrap().contains("stage.sh"));
    running.shutdown().await;

    // A stand-in stage.sh that checks its arguments and prints a verdict.
    let script = dir.path().join("stage.sh");
    std::fs::write(
        &script,
        "#!/bin/sh\nset -e\ntest -f \"$2/package.json\"\necho staging slot $1 >&2\necho '{\"ok\":true,\"compile\":{\"errors\":[]},\"tests\":{\"passed\":2,\"failed\":0,\"names\":[]},\"forbidden\":[]}'\n",
    )
    .unwrap();
    std::fs::set_permissions(&script, std::os::unix::fs::PermissionsExt::from_mode(0o755)).unwrap();
    let pkg_src = dir.path().join("pkgsrc");
    std::fs::create_dir_all(&pkg_src).unwrap();
    std::fs::write(
        pkg_src.join("package.json"),
        "{\"name\":\"com.example.plate\"}",
    )
    .unwrap();
    let tgz = dir.path().join("package.tgz");
    let st = std::process::Command::new("tar")
        .arg("-czf")
        .arg(&tgz)
        .arg("-C")
        .arg(&pkg_src)
        .arg(".")
        .status()
        .unwrap();
    assert!(st.success());
    let s2 = script.clone();
    let running = companion(&node, dir.path(), move |c| c.stage.command = Some(s2)).await;
    let api = Api::new(&running, &node);
    let (h, _) = running
        .state
        .store
        .put(&std::fs::read(&tgz).unwrap(), None)
        .unwrap();
    let (s, job) = api
        .post(
            "/v1/stage",
            json!({"changeSetId": "cs_01J9ZQ00000000000000000012", "packageRef": format!("sha256:{h}")}),
        )
        .await;
    assert_eq!(s, 202, "{job}");
    let id = job["jobId"].as_str().unwrap().to_string();
    let mut last = Value::Null;
    for _ in 0..250 {
        let (_, j) = api.get(&format!("/v1/stage/{id}")).await;
        if j["state"] == "done" || j["state"] == "failed" {
            last = j;
            break;
        }
        tokio::time::sleep(Duration::from_millis(20)).await;
    }
    assert_eq!(last["state"], "done", "{last}");
    assert_eq!(last["slot"], "1");
    assert_eq!(last["verdict"]["ok"], true);
    assert_eq!(last["verdict"]["tests"]["passed"], 2);
    assert_eq!(last["verdict"]["exitCode"], 0);
    running.shutdown().await;
}

/// A change set with an unknown tool: one re-ask (the catalog rules count like the schema).
fn changeset_with_tool(id: &str, tool: &str) -> Vec<u8> {
    serde_json::to_vec(&json!({
        "id": id, "schema": "gamecore.studio.changeset/1",
        "intent": {"text": "Give the ferryman a lantern", "origin": "agent"},
        "operations": [
            {"opId": "op1", "tool": tool, "target": {"kind": "Entity", "authoringId": "e-ferryman"},
             "args": {"item": "item.lantern@2"}}
        ]
    }))
    .unwrap()
}

#[tokio::test]
async fn catalog_rules_are_reasked_and_a_refused_reask_settles_invalid() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    // 1. Unknown tool → re-ask → a valid change set.
    let id = "cs_01J9ZQ00000000000000000020";
    let (s, v) = api.post("/v1/requests", edit_request(id)).await;
    assert_eq!(s, 200, "{v}");
    let first = v["taskId"].as_str().unwrap().to_string();
    node.complete(
        &first,
        &[(
            "changeset.json",
            changeset_with_tool(id, "inventory.grantAll"),
        )],
    );
    let deadline = std::time::Instant::now() + Duration::from_secs(10);
    let v = loop {
        let (_, v) = api.get(&format!("/v1/requests/{id}")).await;
        if v["attempt"] == 1 && v["taskId"].is_string() {
            break v;
        }
        assert!(std::time::Instant::now() < deadline, "{v}");
        tokio::time::sleep(Duration::from_millis(20)).await;
    };
    assert_eq!(v["outcome"]["diagnostics"][0]["code"], "UnknownTool", "{v}");
    let second = v["taskId"].as_str().unwrap().to_string();
    node.complete(
        &second,
        &[(
            "changeset.json",
            changeset_with_tool(id, "inventory.grantStarting"),
        )],
    );
    let v = api.until_state(id, "candidate").await;
    assert_eq!(v["tasks"].as_array().unwrap().len(), 2);
    let (_, c) = api.get(&format!("/v1/candidates/{id}")).await;
    assert_eq!(c["toolCatalogRevision"], catalog_rev(), "{c}");

    // 2. The re-ask is refused by etos: candidate_invalid with the original findings and the refusal.
    let id = "cs_01J9ZQ00000000000000000021";
    node.lock().refuse_ids.push(format!("{id}.r1"));
    let (_, v) = api.post("/v1/requests", edit_request(id)).await;
    let task = v["taskId"].as_str().unwrap().to_string();
    node.complete(
        &task,
        &[(
            "changeset.json",
            changeset_with_tool(id, "inventory.grantAll"),
        )],
    );
    let v = api.until_state(id, "candidate_invalid").await;
    let codes: Vec<&str> = v["outcome"]["diagnostics"]
        .as_array()
        .unwrap()
        .iter()
        .map(|d| d["code"].as_str().unwrap())
        .collect();
    assert_eq!(codes, ["UnknownTool", "Refused"], "{v}");
    assert_eq!(
        v["outcome"]["diagnostics"][1]["data"]["code"], "request_rejected",
        "{v}"
    );
    assert!(!running.state.ledger.candidate(id).is_ok());

    // 3. A plain `failed` record (not a cancel) fails the request.
    let id = "cs_01J9ZQ00000000000000000022";
    let (_, v) = api.post("/v1/requests", edit_request(id)).await;
    let task = v["taskId"].as_str().unwrap().to_string();
    let topic = node
        .tasks()
        .into_iter()
        .find(|t| t.id == task)
        .unwrap()
        .topic;
    node.post_record(
        &topic,
        "gc-designer@fake",
        "the model failed (etk_secretsecretsecret)",
        Some("failed"),
        vec![],
    );
    let v = api.until_state(id, "failed").await;
    assert_eq!(v["outcome"]["code"], "task_failed");
    assert!(
        !v["outcome"]["message"]
            .as_str()
            .unwrap()
            .contains("etk_secret"),
        "redacted: {v}"
    );
    running.shutdown().await;
}

#[tokio::test]
async fn requests_are_scoped_to_the_calling_app_and_rejections_are_etos_shaped() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| {
        c.allowed_apps = vec!["gamecore-unity".into(), "other-app".into()];
    })
    .await;
    let api = Api::new(&running, &node);
    let other = api.as_app("other-app");
    let id = "cs_01J9ZQ00000000000000000030";
    let (s, _) = api.post("/v1/requests", edit_request(id)).await;
    assert_eq!(s, 200);
    let (s, v) = other.get(&format!("/v1/requests/{id}")).await;
    assert_eq!((s, v["code"].as_str()), (404, Some("not_found")), "{v}");
    let (s, _) = other
        .post(&format!("/v1/requests/{id}/cancel"), json!({}))
        .await;
    assert_eq!(s, 404);
    let (s, _) = other.get(&format!("/v1/candidates/{id}")).await;
    assert_eq!(s, 404);
    let (_, list) = other.get("/v1/requests?after=0").await;
    assert!(list["requests"].as_array().unwrap().is_empty(), "{list}");
    let (_, mine) = api.get("/v1/requests?after=0").await;
    assert_eq!(mine["requests"].as_array().unwrap().len(), 1);
    // Answers carry no null members.
    let (_, view) = api.get(&format!("/v1/requests/{id}")).await;
    assert!(gamecore_studio::util::first_null(&view).is_none(), "{view}");
    // Framework rejections are {code, message, hint}.
    for (method, path, status) in [
        (reqwest::Method::GET, "/v1/requests?after=abc", 400u16),
        (reqwest::Method::PUT, "/v1/hello", 405),
        (reqwest::Method::GET, "/v1/events", 400),
        (reqwest::Method::POST, "/v1/requests", 400),
    ] {
        let (s, ct, v) = api.raw(method.clone(), path, b"{not json".to_vec()).await;
        assert!(ct.starts_with("application/json"), "{method} {path}: {ct}");
        assert!(
            v["code"].is_string() && v["message"].is_string(),
            "{method} {path}: {v}"
        );
        if path != "/v1/events" {
            assert_eq!(s, status, "{method} {path}: {v}");
        } else {
            assert!(s == 400 || s == 426, "{s}: {v}");
        }
    }
    running.shutdown().await;
}

#[tokio::test]
async fn unresolved_requests_are_followed_at_most_three_more_times() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let id = "cs_01J9ZQ00000000000000000040";
    {
        let running = companion(&node, dir.path(), |_| {}).await;
        let l = &running.state.ledger;
        l.insert_request(&gamecore_studio::ledger::NewRequest {
            change_set_id: id.into(),
            digest: "d".into(),
            body: edit_request(id),
            app: "gamecore-unity".into(),
            worker: "gc-designer".into(),
            etos_request_id: id.into(),
            topic: "#agent/gamecore-studio/cs-gone".into(),
        })
        .unwrap();
        l.set_attempt_task(id, 0, "t-gone").unwrap();
        l.update_request(
            id,
            &gamecore_studio::ledger::RequestUpdate {
                state: Some(gamecore_studio::model::RequestState::Unresolved),
                ..Default::default()
            },
        )
        .unwrap();
        running.shutdown().await;
    }
    for round in 1..=4 {
        let running = companion(&node, dir.path(), |_| {}).await;
        let row = running.state.ledger.request(id).unwrap();
        let gave_up = row.outcome.as_ref().is_some_and(|o| o["gaveUp"] == true);
        assert_eq!(gave_up, round == 4, "round {round}: {:?}", row.outcome);
        assert_eq!(running.state.desk.following(), usize::from(round < 4));
        running.shutdown().await;
    }
}

#[tokio::test]
async fn a_slow_task_open_is_reread_not_failed() {
    let node = FakeNode::start().await;
    {
        let mut g = node.lock();
        g.slow_opens = 1;
        g.open_delay_ms = 2_500;
    }
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |c| c.task_open_timeout_secs = 1).await;
    let api = Api::new(&running, &node);
    let id = "cs_01J9ZQ00000000000000000050";
    let (s, v) = api.post("/v1/requests", edit_request(id)).await;
    assert_eq!(s, 200, "{v}");
    assert_ne!(
        v["state"], "failed",
        "a timed-out open is not a failure: {v}"
    );
    // The open timed out after 1 s; the desk asks again by request id and gets the task
    // etos had already opened. One task, never a second.
    let deadline = std::time::Instant::now() + Duration::from_secs(20);
    let v = loop {
        let (_, v) = api.get(&format!("/v1/requests/{id}")).await;
        if v["taskId"].is_string() {
            break v;
        }
        assert!(std::time::Instant::now() < deadline, "{v}");
        tokio::time::sleep(Duration::from_millis(50)).await;
    };
    assert_eq!(v["state"], "running", "{v}");
    assert_eq!(node.tasks().len(), 1);
    assert!(node.calls("POST", "/tasks") >= 2, "re-read by request id");
    let task = v["taskId"].as_str().unwrap().to_string();
    node.complete(
        &task,
        &[(
            "changeset.json",
            changeset_with_tool(id, "inventory.grantStarting"),
        )],
    );
    api.until_state(id, "candidate").await;
    running.shutdown().await;
}

#[tokio::test]
async fn a_degraded_model_is_named_in_the_outcome() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let running = companion(&node, dir.path(), |_| {}).await;
    let api = Api::new(&running, &node);
    let id = "cs_01J9ZQ00000000000000000051";
    let (_, v) = api.post("/v1/requests", edit_request(id)).await;
    let task = v["taskId"].as_str().unwrap().to_string();
    let topic = node
        .tasks()
        .into_iter()
        .find(|t| t.id == task)
        .unwrap()
        .topic;
    node.lock().task_error = Some(
        "HTTP 503: auth_unavailable: no auth available (providers=claude, model=claude-opus-5-5)"
            .into(),
    );
    node.post_record(
        &topic,
        "gc-designer@fake",
        "the turn failed",
        Some("failed"),
        vec![],
    );
    let v = api.until_state(id, "failed").await;
    assert_eq!(v["outcome"]["code"], "task_failed", "{v}");
    assert_eq!(v["outcome"]["reason"], "model_degraded", "{v}");
    assert!(
        v["outcome"]["message"]
            .as_str()
            .unwrap()
            .contains("auth_unavailable"),
        "{v}"
    );
    running.shutdown().await;
}
