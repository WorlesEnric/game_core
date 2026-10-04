//! The Unity-facing API (04 §2), served on the loopback listener the node's proxy reaches.
//!
//! Every request must carry the node's current proxy token (`X-Etos-Proxy-Token`, compared in
//! constant time with the token of the current welcome, which changes when etosd restarts)
//! and an allowed calling app (`X-Etos-App`); otherwise `403 forbidden`. Before the first
//! welcome the answer is `503 agent_starting`. Errors are `{code, message, hint}`.
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
    CandidateView, Diagnostic, GenerateRequest, Hello, IndexDelta, RequestList, StageRequest,
    StoredArtifact,
};
use crate::ops::MediaOps;
use crate::stage::StageRunner;
use crate::store::ArtifactStore;
use crate::util::normalize_sha256;
use crate::voice::VoiceBridge;

/// Largest request body (a request with attachments, an index delta).
pub const MAX_BODY: usize = 160 * 1024 * 1024;

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
        .route("/v1/stage/{job}", get(stage_job))
        .fallback(|| async { ApiError::not_found("no such companion route").into_response() })
        .layer(middleware::from_fn_with_state(state.clone(), authenticate))
        .layer(DefaultBodyLimit::max(MAX_BODY))
        .with_state(state)
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
    req.extensions_mut().insert(Caller(app));
    next.run(req).await
}

fn parse<T: DeserializeOwned>(body: &Bytes) -> ApiResult<T> {
    serde_json::from_slice(body)
        .map_err(|e| ApiError::bad_request(format!("the body does not fit: {e}")))
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
) -> ApiResult<Json<Hello>> {
    let welcome = s.agent.welcome();
    let (providers, checked) = s.ops.providers().await;
    Ok(Json(Hello {
        service: s.cfg.agent.clone(),
        version: crate::VERSION.to_string(),
        protocol: crate::PROTOCOL,
        app,
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
        ]
        .iter()
        .map(|c| c.to_string())
        .collect(),
        providers,
        providers_checked_at: checked,
        workers: s.cfg.workers.clone(),
        tool_catalog_revisions: s.ledger.catalog_revisions()?,
        index_revision: s.indexer.revision(),
    }))
}

async fn submit(
    State(s): State<AppState>,
    Extension(Caller(app)): Extension<Caller>,
    body: Bytes,
) -> ApiResult<Response> {
    let raw: Value = parse(&body)?;
    let view = s.desk.submit(raw, &app).await?;
    Ok((
        StatusCode::OK,
        Json(json!({
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
    Query(q): Query<ListQuery>,
) -> ApiResult<Json<RequestList>> {
    let limit = q.limit.unwrap_or(200).clamp(1, 1000);
    let requests = s.ledger.requests_after(q.after, limit)?;
    let next = requests.last().map(|r| r.seq).unwrap_or(q.after);
    Ok(Json(RequestList { requests, next }))
}

async fn read_request(State(s): State<AppState>, Path(id): Path<String>) -> ApiResult<Response> {
    let view = s
        .ledger
        .request_view(&id)
        .map_err(|e| not_found_or(e, || format!("no request {id}")))?;
    Ok(Json(view).into_response())
}

async fn cancel(State(s): State<AppState>, Path(id): Path<String>) -> ApiResult<Response> {
    let view = s.desk.cancel(&id).await?;
    Ok(Json(view).into_response())
}

async fn candidate(
    State(s): State<AppState>,
    Path(id): Path<String>,
) -> ApiResult<Json<CandidateView>> {
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
    Ok(Json(CandidateView {
        change_set_id: c.change_set_id,
        task_id: c.task_id,
        attempt: c.attempt,
        change_set: c.change_set,
        artifacts,
        diagnostics,
        received_at: c.received_at,
    }))
}

async fn artifact(State(s): State<AppState>, Path(sha): Path<String>) -> ApiResult<Response> {
    let h = normalize_sha256(&sha).ok_or_else(|| ApiError::bad_request("not a sha256 digest"))?;
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

async fn index_delta(State(s): State<AppState>, body: Bytes) -> ApiResult<Response> {
    let delta: IndexDelta = parse(&body)?;
    let ack = s.indexer.ingest(&delta).map_err(|e| match e {
        IndexError::Backpressure(_) => ApiError::new(
            StatusCode::SERVICE_UNAVAILABLE,
            "backpressure",
            e.to_string(),
        )
        .with_hint("the node is unreachable or refused the binding; deltas resume when it answers"),
        IndexError::Poisoned => ApiError::internal(e.to_string()),
    })?;
    Ok(Json(ack).into_response())
}

async fn generate(State(s): State<AppState>, body: Bytes) -> ApiResult<Response> {
    let req: GenerateRequest = parse(&body)?;
    let out = s.ops.generate(req).await?;
    Ok(Json(out).into_response())
}

async fn stage(State(s): State<AppState>, body: Bytes) -> ApiResult<Response> {
    let req: StageRequest = parse(&body)?;
    let job = s.stage.submit(&req)?;
    Ok((StatusCode::ACCEPTED, Json(job)).into_response())
}

async fn stage_job(State(s): State<AppState>, Path(job): Path<String>) -> ApiResult<Response> {
    let j = s
        .ledger
        .stage(&job)
        .map_err(|e| not_found_or(e, || format!("no stage job {job}")))?;
    Ok(Json(j).into_response())
}

#[derive(Debug, Deserialize)]
struct AfterQuery {
    #[serde(default)]
    after: i64,
}

async fn events(
    State(s): State<AppState>,
    Query(q): Query<AfterQuery>,
    ws: WebSocketUpgrade,
) -> Response {
    ws.on_upgrade(move |socket| events_loop(s, socket, q.after))
}

async fn events_loop(s: AppState, socket: WebSocket, mut after: i64) {
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
            let text = serde_json::to_string(&e).unwrap_or_default();
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
    ws.on_upgrade(move |socket| bridge.run(socket, app))
}
