//! The Unity-facing API (04 §2), served on the loopback listener the node's proxy reaches.
//!
//! Every request must carry the node's current proxy token (`X-Etos-Proxy-Token`, compared in
//! constant time with the token of the current welcome, which changes when etosd restarts)
//! and an allowed calling app (`X-Etos-App`); otherwise `403 forbidden`. Before the first
//! welcome the answer is `503 agent_starting`. Errors are `{code, message, hint}`, including
//! the framework's own rejections (an oversized body, a malformed query, a missing WebSocket
//! upgrade, an unknown method). Requests are scoped to the calling app: another app's
//! request is `404`.
//!
//! Null policy (03 §9): JSON bodies with a `null` anywhere are refused (`400`); answers and
//! WebSocket frames never carry `null` members.
//!
//! | Route | Handler |
//! |---|---|
//! | `GET /v1/hello` | version, capabilities, provider status |
//! | `POST /v1/requests` · `GET /v1/requests?after=&limit=` | submit (idempotent) · list changes |
//! | `GET /v1/requests/{id}` · `POST /v1/requests/{id}/cancel` | read · cancel |
//! | `GET /v1/candidates/{id}` | validated change set + artifacts |
//! | `GET /v1/artifacts/{sha256}` | bytes |
//! | `POST /v1/index/delta` | semantic index delta → Resource Graph |
//! | `POST /v1/ops/generate` | media operation |
//! | `GET /v1/events?after=` (WebSocket) | ordered events with cursors |
//! | `GET /v1/voice` (WebSocket) | voice bridge |
//! | `POST /v1/stage` · `GET /v1/stage/{job}` | staging job · verdict |

use std::sync::Arc;
use std::time::Duration;

use axum::body::{Body, Bytes};
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{DefaultBodyLimit, Path, Query, Request, State};
use axum::http::{HeaderValue, StatusCode, header};
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::{get, post};
use axum::{Extension, Json, Router};
use etos_sdk::{Agent, ProxyToken};
use futures::{SinkExt, StreamExt};
use serde::Deserialize;
use serde::de::DeserializeOwned;
use serde_json::{Value, json};

use crate::config::Config;
use crate::desk::Desk;
use crate::error::{ApiError, ApiResult};
use crate::events::EventHub;
use crate::index::{IndexError, Indexer};
use crate::ledger::{Ledger, LedgerError};
use crate::model::{
    CandidateView, Diagnostic, GenerateRequest, Hello, IndexDelta, RequestList, StoredArtifact,
};
use crate::ops::MediaOps;
use crate::stage::StageRunner;
use crate::store::ArtifactStore;
use crate::util::{first_null, normalize_sha256, pruned};
use crate::voice::VoiceBridge;

/// Largest request body (a request with attachments, an index delta); see
/// [`crate::desk::MAX_BODY`].
pub const MAX_BODY: usize = crate::desk::MAX_BODY;

/// A JSON answer without `null` members.
fn out<T: serde::Serialize>(v: &T) -> Json<Value> {
    Json(pruned(serde_json::to_value(v).unwrap_or(Value::Null)))
}

/// Everything the handlers share.
pub struct Shared {
    /// Configuration.
    pub cfg: Arc<Config>,
    /// The connected agent (welcome, proxy token).
    pub agent: Agent,
    /// Ledger.
    pub ledger: Arc<Ledger>,
    /// Events.
    pub hub: EventHub,
    /// Content store.
    pub store: ArtifactStore,
    /// Task desk.
    pub desk: Arc<Desk>,
    /// Media operations.
    pub ops: Arc<MediaOps>,
    /// Voice bridge.
    pub voice: Arc<VoiceBridge>,
    /// Index logger.
    pub indexer: Arc<Indexer>,
    /// Staging.
    pub stage: Arc<StageRunner>,
}

/// The router's state.
pub type AppState = Arc<Shared>;

/// The authenticated calling app.
#[derive(Debug, Clone)]
pub struct Caller(pub String);

/// The Unity-facing router.
pub fn router(state: AppState) -> Router {
    Router::new()
        .route("/v1/hello", get(hello))
        .route("/v1/requests", post(submit).get(list))
        .route("/v1/requests/{id}", get(read_request))
        .route("/v1/requests/{id}/cancel", post(cancel))
        .route("/v1/candidates/{id}", get(candidate))
        .route("/v1/artifacts/{sha256}", get(artifact))
        .route("/v1/index/delta", post(index_delta))
        .route("/v1/ops/generate", post(generate))
        .route("/v1/events", get(events))
        .route("/v1/voice", get(voice))
        .route("/v1/stage", post(stage))
        .route(
            "/v1/stage/app-candidate",
            post(crate::stage::app_candidate::submit),
        )
        .route("/v1/stage/{job}", get(stage_job))
        .route("/v1/stage/{job}/cancel", post(stage_cancel))
        .route("/v1/stage/{job}/verdict", get(stage_verdict))
        .route("/v1/stage/{job}/verify", post(stage_verify))
        .fallback(|| async { ApiError::not_found("no such companion route").into_response() })
        .layer(middleware::from_fn_with_state(state.clone(), authenticate))
        .layer(DefaultBodyLimit::max(MAX_BODY))
        .layer(middleware::map_response(etos_shaped))
        .with_state(state)
}

/// Turn a framework rejection (plain text: 400/405/413/415/422/426 ...) into
/// `{code, message, hint}`. Answers that are JSON already, and successes, pass unchanged.
async fn etos_shaped(res: Response) -> Response {
    let status = res.status();
    let is_json = res
        .headers()
        .get(header::CONTENT_TYPE)
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.starts_with("application/json"));
    if !(status.is_client_error() || status.is_server_error()) || is_json {
        return res;
    }
    let text = axum::body::to_bytes(res.into_body(), 4096)
        .await
        .map(|b| String::from_utf8_lossy(&b).trim().to_string())
        .unwrap_or_default();
    let (code, hint) = match status {
        StatusCode::PAYLOAD_TOO_LARGE => (
            "too_large",
            format!("a request body is at most {} MiB", MAX_BODY >> 20),
        ),
        StatusCode::METHOD_NOT_ALLOWED => {
            ("bad_request", "see 04 §2 for the routes and methods".into())
        }
        StatusCode::UPGRADE_REQUIRED => ("bad_request", "open this route as a WebSocket".into()),
        StatusCode::NOT_FOUND => ("not_found", "see 04 §2 for the routes".into()),
        s if s.is_server_error() => ("internal", "retry; the companion logs the cause".into()),
        _ => ("bad_request", "see 04 §2 for the request shapes".into()),
    };
    let message = if text.is_empty() {
        status.canonical_reason().unwrap_or("rejected").to_string()
    } else {
        text
    };
    ApiError::new(status, code, message)
        .with_hint(hint)
        .into_response()
}

/// Check the proxy token against the current welcome and the calling app.
async fn authenticate(State(s): State<AppState>, mut req: Request, next: Next) -> Response {
    let Some(welcome) = s.agent.welcome() else {
        return ApiError::new(
            StatusCode::SERVICE_UNAVAILABLE,
            "agent_starting",
            "the companion has not been welcomed by the node yet",
        )
        .with_hint("try again in a few seconds")
        .into_response();
    };
    let token = welcome.proxy_token();
    let given = req
        .headers()
        .get(ProxyToken::HEADER)
        .and_then(|v| v.to_str().ok());
    if !token.check(given) {
        tracing::warn!(path = %req.uri().path(), "request without the current proxy token refused");
        return ApiError::forbidden("only the etos node's proxy may call this listener")
            .with_hint("call /api/v1/agents/gamecore-studio/http/... on the node with an app key")
            .into_response();
    }
    let app = req
        .headers()
        .get(ProxyToken::APP_HEADER)
        .and_then(|v| v.to_str().ok())
        .unwrap_or_default()
        .to_string();
    if !s.cfg.allowed_apps.iter().any(|a| a == &app) {
        return ApiError::forbidden(format!("the app {app:?} may not use the companion"))
            .with_hint(format!("allowed: {}", s.cfg.allowed_apps.join(", ")))
            .into_response();
    }
    let project = req
        .headers()
        .get("X-GameCore-Project")
        .and_then(|v| v.to_str().ok())
        .and_then(normalize_sha256);
    // Hello is authenticated but does not require project authority: old clients must
    // be able to discover the contract before attempting scoped routes.
    let project = project.or_else(|| (req.uri().path() == "/v1/hello").then(String::new));
    let Some(project) = project else {
        return ApiError::new(
            StatusCode::UPGRADE_REQUIRED,
            "client_upgrade_required",
            "X-GameCore-Project is required by Studio contract 2",
        )
        .with_hint("update the Studio packages to client revision 4635746 or later")
        .into_response();
    };
    // JSON tuple encoding cannot collide if an app name contains separators.
    req.extensions_mut()
        .insert(Caller(json!([app, project]).to_string()));
    crate::blocking::spawn(async move { next.run(req).await })
        .await
        .unwrap_or_else(|_| ApiError::internal("service lane interrupted").into_response())
}

/// Read a JSON body: refused when it is not JSON, holds a `null` (03 §9), or does not fit `T`.
fn parse_value(body: &Bytes) -> ApiResult<Value> {
    let v: Value = serde_json::from_slice(body)
        .map_err(|e| ApiError::bad_request(format!("the body is not JSON: {e}")))?;
    if let Some(at) = first_null(&v) {
        return Err(ApiError::bad_request(format!("null at {at}"))
            .with_hint("optional members are omitted when absent, never sent as null"));
    }
    Ok(v)
}

fn parse<T: DeserializeOwned>(body: &Bytes) -> ApiResult<T> {
    serde_json::from_value(parse_value(body)?)
        .map_err(|e| ApiError::bad_request(format!("the body does not fit: {e}")))
}

/// The request `id` of the calling app (another app's request is not found).
fn own_request(s: &AppState, id: &str, app: &str) -> ApiResult<crate::ledger::RequestRow> {
    match s.ledger.request(id) {
        Ok(r) if r.app == app => Ok(r),
        Ok(_) | Err(LedgerError::NotFound(_)) => {
            Err(ApiError::not_found(format!("no request {id}")))
        }
        Err(e) => Err(e.into()),
    }
}

fn not_found_or(e: LedgerError, what: impl FnOnce() -> String) -> ApiError {
    match e {
        LedgerError::NotFound(_) => ApiError::not_found(what()),
        other => other.into(),
    }
}

async fn hello(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
) -> ApiResult<Json<Value>> {
    let welcome = s.agent.welcome();
    let (providers, checked) = s.ops.providers().await;
    Ok(out(&Hello {
        tariffs: s.ops.tariffs(),
        service: s.cfg.agent.clone(),
        version: crate::VERSION.to_string(),
        protocol: crate::PROTOCOL,
        minimum_client_contract: 2,
        minimum_client_revision: "4635746".into(),
        app: serde_json::from_str::<Value>(&app)
            .ok()
            .and_then(|v| v[0].as_str().map(str::to_string))
            .unwrap_or_default(),
        node: welcome.as_ref().map(|w| w.node.clone()),
        sdk: welcome.as_ref().map(|w| w.sdk.clone()),
        connected: s.agent.is_connected(),
        capabilities: [
            "requests",
            "candidates",
            "artifacts",
            "index",
            "ops",
            "events",
            "voice",
            "stage",
            "stage.cancel/1",
            "stage.app-candidate.signed/1",
        ]
        .iter()
        .map(|c| c.to_string())
        .collect(),
        providers,
        providers_checked_at: checked,
        workers: s.cfg.workers.clone(),
        tool_catalog_revisions: s.ledger.owned_catalog_revisions(&app)?,
        index_revision: s
            .ledger
            .meta(&format!("index-revision:{app}"))?
            .and_then(|v| v.parse().ok()),
    }))
}

async fn submit(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    body: Bytes,
) -> ApiResult<Response> {
    let raw: Value = parse_value(&body)?;
    let view = s.desk.submit(raw, &app).await?;
    Ok((
        StatusCode::OK,
        out(&json!({
            "requestId": view.request_id,
            "changeSetId": view.change_set_id,
            "taskId": view.task_id,
            "state": view.state,
            "taskStatus": view.task_status,
            "request": view,
        })),
    )
        .into_response())
}

#[derive(Debug, Deserialize)]
struct ListQuery {
    #[serde(default)]
    after: i64,
    #[serde(default)]
    limit: Option<usize>,
}

async fn list(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Query(q): Query<ListQuery>,
) -> ApiResult<Json<Value>> {
    let limit = q.limit.unwrap_or(200).clamp(1, 1000);
    let requests = s.ledger.requests_after(&app, q.after, limit)?;
    let next = requests.last().map(|r| r.seq).unwrap_or(q.after);
    Ok(out(&RequestList { requests, next }))
}

async fn read_request(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(id): Path<String>,
) -> ApiResult<Response> {
    own_request(&s, &id, &app)?;
    let view = s
        .ledger
        .request_view(&id)
        .map_err(|e| not_found_or(e, || format!("no request {id}")))?;
    Ok(out(&view).into_response())
}

async fn cancel(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(id): Path<String>,
) -> ApiResult<Response> {
    let view = s.desk.cancel(&id, &app).await?;
    Ok(out(&view).into_response())
}

async fn candidate(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(id): Path<String>,
) -> ApiResult<Json<Value>> {
    let row = own_request(&s, &id, &app)?;
    let c = s.ledger.candidate(&id).map_err(|e| match e {
        LedgerError::NotFound(_) => {
            let state = s
                .ledger
                .request(&id)
                .map(|r| r.state.as_str().to_string())
                .ok();
            let mut err = ApiError::not_found(format!("no candidate for {id}"));
            if let Some(st) = state {
                err = err.with_hint(format!("the request is {st}"));
            }
            err
        }
        other => other.into(),
    })?;
    let artifacts: Vec<StoredArtifact> = serde_json::from_value(c.artifacts).unwrap_or_default();
    let diagnostics: Vec<Diagnostic> = serde_json::from_value(c.diagnostics).unwrap_or_default();
    let tool_catalog_revision = row
        .body
        .get("toolCatalogRevision")
        .and_then(Value::as_str)
        .and_then(normalize_sha256)
        .unwrap_or_default();
    Ok(out(&CandidateView {
        change_set_id: c.change_set_id,
        task_id: c.task_id,
        attempt: c.attempt,
        change_set: c.change_set,
        artifacts,
        tool_catalog_revision,
        diagnostics,
        received_at: c.received_at,
    }))
}

async fn artifact(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(sha): Path<String>,
) -> ApiResult<Response> {
    let h = normalize_sha256(&sha).ok_or_else(|| ApiError::bad_request("not a sha256 digest"))?;
    if !s.ledger.owns("artifact", &h, &app)? {
        return Err(ApiError::not_found("no artifact"));
    }
    let bytes = s
        .store
        .get(&h)
        .map_err(|e| ApiError::internal(e.to_string()))?
        .ok_or_else(|| ApiError::not_found(format!("no artifact {h}")))?;
    let media = s
        .ledger
        .artifact(&h)
        .map(|a| a.media_type)
        .unwrap_or_else(|_| "application/octet-stream".into());
    let mut res = Response::new(Body::from(bytes));
    let headers = res.headers_mut();
    headers.insert(
        header::CONTENT_TYPE,
        HeaderValue::from_str(&media)
            .unwrap_or(HeaderValue::from_static("application/octet-stream")),
    );
    if let Ok(v) = HeaderValue::from_str(&h) {
        headers.insert("x-content-sha256", v);
    }
    Ok(res)
}

async fn index_delta(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    body: Bytes,
) -> ApiResult<Response> {
    let delta: IndexDelta = parse(&body)?;
    let ack = s.indexer.ingest_owned(&delta, &app).map_err(|e| match e {
        IndexError::Backpressure(_) => ApiError::new(
            StatusCode::SERVICE_UNAVAILABLE,
            "backpressure",
            e.to_string(),
        )
        .with_hint("the node is unreachable or refused the binding; deltas resume when it answers"),
        IndexError::Poisoned | IndexError::Storage(_) => ApiError::internal(e.to_string()),
    })?;
    s.ledger.set_meta(
        &format!("index-revision:{app}"),
        &delta.revision.to_string(),
    )?;
    Ok(out(&ack).into_response())
}

async fn generate(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    body: Bytes,
) -> ApiResult<Response> {
    let req: GenerateRequest = parse(&body)?;
    if let Some(id) = &req.change_set_id {
        own_request(&s, id, &app)?;
    }
    let answer = s.ops.generate(req, &app).await?;
    Ok(out(&answer).into_response())
}

async fn stage(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    body: Bytes,
) -> ApiResult<Response> {
    let body = parse_value(&body)?;
    let id = body["changeSetId"]
        .as_str()
        .ok_or_else(|| ApiError::bad_request("changeSetId required"))?;
    if s.ledger.request(id).is_ok() {
        own_request(&s, id, &app)?;
    }
    let answer = s.stage.request_owned(body, &app)?;
    Ok((answer.status, out(&answer.body)).into_response())
}

async fn stage_job(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(job): Path<String>,
) -> ApiResult<Response> {
    let j = s
        .ledger
        .stage(&job)
        .map_err(|e| not_found_or(e, || format!("no stage job {job}")))?;
    own_request(&s, &j.change_set_id, &app)?;
    Ok(out(&j).into_response())
}

async fn stage_cancel(
    State(s): State<AppState>,
    Extension(Caller(owner)): Extension<Caller>,
    Path(job): Path<String>,
) -> ApiResult<Response> {
    let runner = s.stage.clone();
    let row = tokio::task::spawn_blocking(move || runner.cancel_owned(&job, &owner))
        .await
        .map_err(|error| ApiError::internal(error.to_string()))??;
    Ok(out(&row).into_response())
}

async fn stage_verdict(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(job): Path<String>,
) -> ApiResult<Response> {
    let row = s.ledger.stage(&job)?;
    own_request(&s, &row.change_set_id, &app)?;
    Ok(out(&s.stage.signed_verdict(&job)?).into_response())
}

async fn stage_verify(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Path(job): Path<String>,
    body: Bytes,
) -> ApiResult<Response> {
    let row = s.ledger.stage(&job)?;
    own_request(&s, &row.change_set_id, &app)?;
    let record = parse_value(&body)?;
    Ok(out(&json!({"verified": s.stage.verify_verdict(&job, &record)?})).into_response())
}

#[derive(Debug, Deserialize)]
struct AfterQuery {
    #[serde(default)]
    after: i64,
}

async fn events(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    Query(q): Query<AfterQuery>,
    ws: WebSocketUpgrade,
) -> Response {
    ws.on_upgrade(move |socket| async move {
        let _ = crate::blocking::spawn(events_loop(s, socket, q.after, app)).await;
    })
}

async fn events_loop(s: AppState, socket: WebSocket, mut after: i64, app: String) {
    let (mut tx, mut rx) = socket.split();
    let mut latest = s.hub.subscribe();
    loop {
        let batch = match s.ledger.events_after(after, 500) {
            Ok(b) => b,
            Err(e) => {
                tracing::error!(error = %e, "cannot read events");
                break;
            }
        };
        let full = batch.len() == 500;
        for e in batch {
            after = e.cursor;
            if !s.ledger.owns_event(&e, &app).unwrap_or(false) {
                continue;
            }
            let text =
                serde_json::to_string(&pruned(serde_json::to_value(&e).unwrap_or(Value::Null)))
                    .unwrap_or_default();
            if tx.send(Message::Text(text.into())).await.is_err() {
                return;
            }
        }
        if full {
            continue;
        }
        tokio::select! {
            changed = latest.changed() => {
                if changed.is_err() {
                    break;
                }
            }
            msg = rx.next() => match msg {
                None | Some(Err(_)) | Some(Ok(Message::Close(_))) => break,
                Some(Ok(_)) => {}
            },
            _ = tokio::time::sleep(Duration::from_secs(20)) => {
                if tx.send(Message::Ping(Bytes::from_static(b"gc"))).await.is_err() {
                    break;
                }
            }
        }
    }
    let _ = tx.close().await;
}

async fn voice(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    ws: WebSocketUpgrade,
) -> Response {
    let bridge = s.voice.clone();
    ws.on_upgrade(move |socket| async move {
        let _ = crate::blocking::spawn(async move { bridge.run(socket, app).await }).await;
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn framework_rejections_become_etos_errors() {
        let plain = Response::builder()
            .status(StatusCode::PAYLOAD_TOO_LARGE)
            .header(header::CONTENT_TYPE, "text/plain; charset=utf-8")
            .body(Body::from("length limit exceeded"))
            .unwrap_or_default();
        let res = etos_shaped(plain).await;
        assert_eq!(res.status(), StatusCode::PAYLOAD_TOO_LARGE);
        let bytes = axum::body::to_bytes(res.into_body(), 4096)
            .await
            .unwrap_or_default();
        let v: Value = serde_json::from_slice(&bytes).unwrap_or(Value::Null);
        assert_eq!(v["code"], "too_large", "{v}");
        assert_eq!(v["message"], "length limit exceeded");
        assert!(v["hint"].is_string());
        // JSON answers and successes pass unchanged.
        let ok = Response::new(Body::from("fine"));
        assert_eq!(etos_shaped(ok).await.status(), StatusCode::OK);
        let json = ApiError::stale_context("x").into_response();
        let bytes = axum::body::to_bytes(etos_shaped(json).await.into_body(), 4096)
            .await
            .unwrap_or_default();
        let v: Value = serde_json::from_slice(&bytes).unwrap_or(Value::Null);
        assert_eq!(v["code"], "stale_context");
    }
}
