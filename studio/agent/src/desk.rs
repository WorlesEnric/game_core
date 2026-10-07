//! The task desk (04 §3): requests become etos tasks on the Studio workers, and finished
//! tasks become candidates.
//!
//! 1. **Submit** (`POST /v1/requests`): validate (the selection, the context slice and the
//!    tool catalog against their schemas; the catalog's digest against `toolCatalogRevision`),
//!    persist the request (idempotent on `changeSetId`: the digest covers the body without
//!    `toolCatalog` and with attachments reduced to `{name, sha256}`; a different request
//!    under the same id is `ledger_conflict`), then open the task.
//! 2. **Open**: pack `request.md`, `selection.json`, `index-slice.json` (capped, truncation
//!    reported), `tool-catalog.json` and the attachments, upload them (`POST /files`), and
//!    `POST /tasks {worker, text, topic: #agent/<agent>/cs-<lowercase ULID>, inputs,
//!    id: <changeSetId>}`. Opening is serialised per request; the inputs of an attempt are
//!    written once (the first packer wins) and the task id is stored before anything else
//!    happens, so a request that already has a task id is never opened again (etos also
//!    returns the same task for the same `id`).
//! 3. **Follow**: a background task long-polls the topic from the saved cursor. Progress
//!    records become `task_progress` events; `waiting` records leave the request `waiting`
//!    with the record text (the budget case; never retried); the final `failed` record fails
//!    it, unless `GET /tasks/{id}` then says `cancelled`; the final `done` record is
//!    evaluated ([`crate::candidate`]). Record texts and etos errors are redacted before they
//!    are stored.
//! 4. **Candidate**: refs are fetched, digests verified against `changeset.json`, the change
//!    set validated against the schema, candidate mode and the bounded catalog rules,
//!    artifacts stored content-addressed, the candidate row and the request's settlement
//!    written in one transaction (never for a request already settled), `candidate` emitted.
//!    An invalid candidate is re-asked once (configurable) as a new task `<changeSetId>.r1`
//!    on topic `cs-<ulid>-r1` whose `request.md` names the parent task and whose inputs add
//!    `diagnostics.json`; the new attempt and the attempt pointer are written together. A
//!    re-ask etos refuses settles `candidate_invalid` with the original diagnostics plus the
//!    refusal. A catalog the companion cannot confirm (`StaleContext`) is not re-asked.
//! 5. **Cancel**: `POST /tasks/{id}/cancel`; the state comes from etos's answer.
//! 6. **Resume**: on startup every non-terminal request is followed again from its cursor;
//!    `GET /tasks/{id}` is re-read when the topic is quiet. An `unresolved` request is
//!    followed again at most [`MAX_UNRESOLVED_RESUMES`] times and for at most
//!    [`UNRESOLVED_MAX_AGE`] after it became unresolved; then the companion gives up on it
//!    (it stays `unresolved`, with `outcome.gaveUp`).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;
use etos_sdk::{Client, RecordStatus, TaskRequest, TopicRecord};
use serde_json::{Map, Value, json};
use tokio::sync::watch;
use tokio::task::JoinHandle;

use crate::candidate::{CatalogContext, Evaluation, Fetched, evaluate, reaskable};
use crate::config::Config;
use crate::error::{ApiError, ApiResult, CANDIDATE_INVALID};
use crate::events::EventHub;
use crate::index::Indexer;
use crate::ledger::{
    ArtifactRow, AttemptRow, CandidateRow, Inserted, Ledger, LedgerError, NewRequest, RequestRow,
    RequestUpdate,
};
use crate::model::{Diagnostic, EditRequest, RequestState, RequestView, StoredArtifact};
use crate::redact::redact;
use crate::schema::{ChangeSetSchema, RequestSchemas};
use crate::store::ArtifactStore;
use crate::util::{
    canonical_json, catalog_revision, normalize_sha256, now_ms, sha256_hex, topic_segment,
    valid_change_set_id,
};

/// Largest attachment accepted, decoded.
pub const MAX_ATTACHMENT: usize = 16 * 1024 * 1024;
/// Most attachments per request.
pub const MAX_ATTACHMENTS: usize = 8;
/// Largest total of a request's attachments, decoded.
pub const MAX_ATTACHMENTS_TOTAL: usize = 64 * 1024 * 1024;
/// Room for everything in a request body besides the attachments' base64 (the selection,
/// the context slice before it is capped, the catalog).
pub const MAX_JSON_BODY: usize = 24 * 1024 * 1024;
/// Largest request body: the attachments' budget as base64 plus [`MAX_JSON_BODY`]. A body
/// within the attachment budget is never refused by the body limit first.
pub const MAX_BODY: usize = 4 * MAX_ATTACHMENTS_TOTAL.div_ceil(3) + MAX_JSON_BODY;
/// Follows of an `unresolved` request after restarts before the companion gives up.
pub const MAX_UNRESOLVED_RESUMES: u32 = 3;
/// How long after becoming `unresolved` a request is still followed after a restart.
pub const UNRESOLVED_MAX_AGE: Duration = Duration::from_secs(24 * 3600);
/// Quiet polls after etos reports `done`/`failed` before the companion stops waiting for the
/// final record.
const FINAL_RECORD_GRACE: u32 = 3;

/// What the follower does after a record.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Flow {
    /// Keep reading.
    Continue,
    /// Re-read the request (its state or attempt changed).
    Reread,
    /// A transient failure: retry the same record later.
    Retry,
    /// Stop following (the request is settled or unresolved).
    Exit,
}

/// The task desk.
pub struct Desk {
    cfg: Arc<Config>,
    client: Client,
    /// `client` with the task-open timeout and no SDK retries: a timed-out open is resolved
    /// by the desk (an idempotent re-open by request id), never by a blind repeat.
    opener: Client,
    ledger: Arc<Ledger>,
    hub: EventHub,
    store: ArtifactStore,
    schema: Arc<ChangeSetSchema>,
    contracts: RequestSchemas,
    indexer: Arc<Indexer>,
    followers: Mutex<HashMap<String, JoinHandle<()>>>,
    opening: Mutex<HashMap<String, Arc<tokio::sync::Mutex<()>>>>,
    stop: watch::Sender<bool>,
}

impl std::fmt::Debug for Desk {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Desk").finish_non_exhaustive()
    }
}

/// How long `POST /v1/requests` waits for the task to open before answering (the open goes
/// on in the background).
const SUBMIT_WAIT: Duration = Duration::from_secs(30);

/// Pause before re-reading a task whose open timed out.
const OPEN_REREAD_DELAY: Duration = Duration::from_secs(5);

/// A `POST /tasks` that timed out on the client side (the SDK's transport error).
fn is_open_timeout(e: &ApiError) -> bool {
    e.code() == "transport" && e.body.message.contains("timed out")
}

/// The reason a task stalled or failed, when its text names a degraded model (etos marks a
/// model degraded after availability failures: 401/503, `auth_unavailable`, ...).
fn model_degraded(text: &str) -> bool {
    let t = text.to_ascii_lowercase();
    t.contains("degraded")
        || t.contains("auth_unavailable")
        || t.contains("access token has been revoked")
        || (t.contains("model")
            && (t.contains("http 401") || t.contains("http 503") || t.contains("unavailable")))
}

/// Add `reason: "model_degraded"` and the model's error text to an outcome when one of the
/// texts names a degraded model.
fn with_model_reason(mut outcome: Value, texts: &[Option<&str>]) -> Value {
    if let Some((t, o)) = texts
        .iter()
        .flatten()
        .find(|t| model_degraded(t))
        .zip(outcome.as_object_mut())
    {
        o.insert("reason".into(), json!("model_degraded"));
        o.insert("modelError".into(), json!(redact(t)));
    }
    outcome
}

fn is_retryable(e: &ApiError) -> bool {
    matches!(e.code(), "transport" | "protocol")
        || e.status.as_u16() == 408
        || e.status.as_u16() == 429
        || e.status.is_server_error()
}

/// The outcome of a request whose task etos refused: the registered outcome code
/// `task_failed` (04 §2), with the refusal as it came (`refusal: {code, status, hint?}`).
fn outcome_of(e: &ApiError, phase: &str) -> Value {
    crate::util::pruned(json!({
        "code": "task_failed",
        "message": e.body.message,
        "phase": phase,
        "refusal": {"code": e.body.code, "status": e.status.as_u16(), "hint": e.body.hint},
    }))
}

/// The diagnostic of a refusal etos gave (`Refused`, with the refusal as `data`).
fn refusal_diagnostic(e: &ApiError, what: &str) -> Diagnostic {
    Diagnostic::new("Refused", format!("{what}: {}", e.body.message)).with_data(
        crate::util::pruned(
            json!({"code": e.body.code, "status": e.status.as_u16(), "hint": e.body.hint}),
        ),
    )
}

/// The context slice without the companion's `rgKind`/`rgKey` node extensions (they are
/// not part of `semantic-index.schema.json` and never reach a worker).
pub fn strip_rg_extensions(slice: &Value) -> Value {
    let mut v = slice.clone();
    if let Some(nodes) = v.get_mut("nodes").and_then(Value::as_array_mut) {
        for n in nodes {
            if let Some(o) = n.as_object_mut() {
                o.remove("rgKind");
                o.remove("rgKey");
            }
        }
    }
    v
}

/// The idempotency digest of a request body: canonical JSON without `toolCatalog` (it is
/// identified by `toolCatalogRevision`) and with each attachment reduced to `{name, sha256}`
/// (the digest of its decoded bytes).
pub fn request_digest(raw: &Value) -> String {
    let mut v = raw.clone();
    if let Some(o) = v.as_object_mut() {
        o.remove("toolCatalog");
        if let Some(Value::Array(atts)) = o.get_mut("attachments") {
            for a in atts.iter_mut() {
                let name = a.get("name").cloned().unwrap_or(Value::Null);
                let sha = a
                    .get("data")
                    .and_then(Value::as_str)
                    .and_then(|d| STANDARD.decode(d.as_bytes()).ok())
                    .map(|b| json!(sha256_hex(&b)))
                    .unwrap_or(Value::Null);
                *a = json!({"name": name, "sha256": sha});
            }
        }
    }
    sha256_hex(canonical_json(&v).as_bytes())
}

/// The index slice under `max` bytes: nodes, then edges, then scopes are dropped from the end
/// until it fits, and `truncated` records what was dropped. `None` when it cannot fit.
pub fn cap_slice(slice: &Value, max: usize) -> Option<(Value, Option<Value>)> {
    let size = |v: &Value| serde_json::to_vec(v).map(|b| b.len()).unwrap_or(usize::MAX);
    if size(slice) <= max {
        return Some((slice.clone(), None));
    }
    let mut v = slice.clone();
    let mut dropped: Map<String, Value> = Map::new();
    for key in ["nodes", "edges", "scopes"] {
        let mut n = 0u64;
        while size(&v) > max {
            let popped = v
                .get_mut(key)
                .and_then(Value::as_array_mut)
                .and_then(|a| a.pop());
            if popped.is_none() {
                break;
            }
            n += 1;
        }
        if n > 0 {
            dropped.insert(key.to_string(), json!(n));
        }
    }
    let info = json!({"droppedFromEnd": dropped, "capBytes": max});
    if let Some(o) = v.as_object_mut() {
        o.insert("truncated".into(), info.clone());
    }
    (size(&v) <= max.saturating_add(256)).then_some((v, Some(info)))
}

impl Desk {
    /// A desk.
    pub fn new(
        cfg: Arc<Config>,
        client: Client,
        ledger: Arc<Ledger>,
        hub: EventHub,
        store: ArtifactStore,
        schema: Arc<ChangeSetSchema>,
        indexer: Arc<Indexer>,
    ) -> Result<Arc<Desk>, String> {
        let (stop, _) = watch::channel(false);
        let opener = client
            .with_timeout(Duration::from_secs(cfg.task_open_timeout_secs))
            .with_retries(0);
        Ok(Arc::new(Desk {
            cfg,
            client,
            opener,
            ledger,
            hub,
            store,
            schema,
            contracts: RequestSchemas::builtin()?,
            indexer,
            followers: Mutex::new(HashMap::new()),
            opening: Mutex::new(HashMap::new()),
            stop,
        }))
    }

    fn open_lock(&self, rid: &str) -> Arc<tokio::sync::Mutex<()>> {
        match self.opening.lock() {
            Ok(mut m) => m.entry(rid.to_string()).or_default().clone(),
            Err(_) => Arc::new(tokio::sync::Mutex::new(())),
        }
    }

    fn topic(&self, change_set_id: &str, attempt: u32) -> String {
        format!(
            "#agent/{}/{}",
            self.cfg.agent,
            topic_segment(change_set_id, attempt)
        )
    }

    fn update(&self, rid: &str, upd: RequestUpdate) -> Result<RequestView, LedgerError> {
        let (view, cursor) = self.ledger.update_request(rid, &upd)?;
        self.hub.notify(cursor);
        self.record_changeset(&view);
        Ok(view)
    }

    fn record_changeset(&self, v: &RequestView) {
        let mut m = Map::new();
        m.insert("state".into(), json!(v.state.as_str()));
        if let Some(o) = &v.outcome {
            m.insert("outcome".into(), o.clone());
        }
        if let Some(t) = &v.task_id {
            m.insert("task".into(), json!(t));
            let mut t_values = Map::new();
            t_values.insert("worker".into(), json!(v.worker));
            if let Some(s) = &v.task_status {
                t_values.insert("status".into(), json!(s));
            }
            t_values.insert("changeset".into(), json!(v.change_set_id));
            self.indexer.record("gc_task", t, t_values);
        }
        self.indexer.record("gc_changeset", &v.change_set_id, m);
    }

    fn emit(&self, kind: &str, rid: &str, data: Value) {
        if let Err(e) = self.hub.emit(kind, Some(rid), &crate::util::pruned(data)) {
            tracing::error!(request = rid, kind, error = %e, "cannot append an event");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Submit.

    /// `POST /v1/requests`.
    pub async fn submit(self: &Arc<Self>, raw: Value, app: &str) -> ApiResult<RequestView> {
        let req: EditRequest = serde_json::from_value(raw.clone())
            .map_err(|e| ApiError::bad_request(format!("not an EditRequest: {e}")))?;
        if !valid_change_set_id(&req.change_set_id) {
            return Err(ApiError::bad_request(
                "changeSetId is not `cs_` plus a 26-character ULID (IdDerivation.NewChangeSetId)",
            ));
        }
        if req.intent.text.trim().is_empty() {
            return Err(ApiError::bad_request("intent.text is empty"));
        }
        let worker = req
            .worker
            .clone()
            .unwrap_or_else(|| self.cfg.default_worker.clone());
        if !self.cfg.workers.contains(&worker) {
            return Err(
                ApiError::bad_request(format!("worker {worker:?} is not a Studio worker"))
                    .with_hint(format!("workers: {}", self.cfg.workers.join(", "))),
            );
        }
        let mut findings = self.contracts.selection.findings(
            raw.get("selection").unwrap_or(&Value::Null),
            "InvalidArgs",
            "selection",
        );
        findings.extend(self.contracts.slice.findings(
            &strip_rg_extensions(&req.context_slice),
            "InvalidArgs",
            "contextSlice",
        ));
        if !findings.is_empty() {
            return Err(ApiError::bad_request(format!(
                "the request does not fit the contract schemas ({} finding(s))",
                findings.len()
            ))
            .with_hint("selection: selection-snapshot.schema.json; contextSlice: semantic-index.schema.json")
            .with_diagnostics(findings));
        }
        let revision = normalize_sha256(&req.tool_catalog_revision).ok_or_else(|| {
            ApiError::bad_request(
                "toolCatalogRevision is the sha256 of the catalog's canonical JSON without `revision`",
            )
        })?;
        match &req.tool_catalog {
            Some(cat) => {
                let findings = self
                    .contracts
                    .catalog
                    .findings(cat, "InvalidArgs", "toolCatalog");
                if !findings.is_empty() {
                    return Err(ApiError::bad_request(
                        "toolCatalog does not fit tool-catalog.schema.json",
                    )
                    .with_diagnostics(findings));
                }
                let actual = catalog_revision(cat);
                let claimed = cat
                    .get("revision")
                    .and_then(Value::as_str)
                    .and_then(normalize_sha256);
                if actual != revision || claimed.is_some_and(|c| c != actual) {
                    return Err(ApiError::bad_request(format!(
                        "toolCatalogRevision is {revision}, but the catalog's digest is {actual}"
                    ))
                    .with_hint("toolCatalogRevision and the catalog's `revision` are the sha256 of its canonical JSON (keys sorted, no whitespace) without `revision`"));
                }
                self.ledger
                    .put_catalog(&revision, &actual, cat)
                    .map_err(|e| match e {
                        LedgerError::Conflict(m) => ApiError::ledger_conflict(m),
                        other => other.into(),
                    })?;
            }
            None => {
                if !self.ledger.owns("catalog", &revision, app)?
                    || self.ledger.catalog(&revision)?.is_none()
                {
                    return Err(ApiError::stale_context(format!(
                        "the companion holds no tool catalog revision {revision}"
                    ))
                    .with_hint("send the request again with `toolCatalog` for this revision"));
                }
            }
        }
        self.ledger.grant("catalog", &revision, app)?;
        if req.attachments.len() > MAX_ATTACHMENTS {
            return Err(ApiError::bad_request(format!(
                "at most {MAX_ATTACHMENTS} attachments"
            )));
        }
        let mut total = 0usize;
        for a in &req.attachments {
            let bytes = STANDARD.decode(a.data.as_bytes()).map_err(|_| {
                ApiError::bad_request(format!("attachment {} is not base64", a.name))
            })?;
            total += bytes.len();
            if bytes.len() > MAX_ATTACHMENT || total > MAX_ATTACHMENTS_TOTAL {
                return Err(ApiError::new(
                    axum::http::StatusCode::PAYLOAD_TOO_LARGE,
                    "too_large",
                    format!(
                        "attachments are at most 16 MiB each and {} MiB together",
                        MAX_ATTACHMENTS_TOTAL >> 20
                    ),
                ));
            }
            if let Some(d) = a
                .sha256
                .as_ref()
                .filter(|d| normalize_sha256(d) != Some(sha256_hex(&bytes)))
            {
                return Err(ApiError::bad_request(format!(
                    "attachment {} does not have sha256 {d}",
                    a.name
                )));
            }
        }
        let digest = request_digest(&raw);
        let new = NewRequest {
            change_set_id: req.change_set_id.clone(),
            digest,
            body: raw,
            app: app.to_string(),
            worker,
            etos_request_id: req.change_set_id.clone(),
            topic: self.topic(&req.change_set_id, 0),
        };
        let inserted = self.ledger.insert_request(&new).map_err(|e| match e {
            LedgerError::Conflict(m) => ApiError::ledger_conflict(m).with_hint(
                "a change-set id names one request; mint a new id for a different request",
            ),
            other => other.into(),
        })?;
        let rid = req.change_set_id.clone();
        match inserted {
            Inserted::Existing(view) => {
                if !view.state.is_terminal() {
                    self.spawn_follower(&rid);
                }
                Ok(view)
            }
            Inserted::Created(view, cursor) => {
                self.hub.notify(cursor);
                self.record_changeset(&view);
                tracing::info!(request = %rid, worker = %view.worker, "request recorded");
                // The open runs detached: answering the HTTP call must never drop a
                // `POST /tasks` in flight (etos launches the task inside that request). The
                // follower waits on the same per-request lock, so it cannot open twice.
                let me = Arc::clone(self);
                let id = rid.clone();
                let mut opening =
                    crate::blocking::spawn(async move { me.open_attempt(&id, 0).await });
                match tokio::time::timeout(SUBMIT_WAIT, &mut opening).await {
                    Ok(Ok(Ok(()))) => {}
                    Ok(Ok(Err(e))) if !is_retryable(&e) => {
                        // Settled by open_attempt; the refusal passes through.
                        return Err(e);
                    }
                    Ok(Ok(Err(e))) => {
                        tracing::warn!(request = %rid, error = %e, "task not opened yet; the follower retries");
                    }
                    Ok(Err(e)) => {
                        tracing::error!(request = %rid, error = %e, "the open task panicked; the follower retries");
                    }
                    Err(_) => {
                        tracing::warn!(request = %rid, "opening the task takes long; it continues in the background");
                    }
                }
                self.spawn_follower(&rid);
                Ok(self.ledger.request_view(&rid)?)
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Open.

    async fn upload(&self, name: &str, media: &str, bytes: &[u8]) -> ApiResult<Value> {
        let info = self.client.files().put(name, media, bytes).await?;
        let ours = sha256_hex(bytes);
        if let Some(theirs) = normalize_sha256(&info.digest).filter(|theirs| *theirs != ours) {
            return Err(ApiError::new(
                axum::http::StatusCode::BAD_GATEWAY,
                "protocol",
                format!("the node stored {name} with digest {theirs}, the bytes sent are {ours}"),
            ));
        }
        let media = if info.media_type.is_empty() {
            media.to_string()
        } else {
            info.media_type.clone()
        };
        Ok(
            json!({"name": name, "ref": info.reference.id, "digest": format!("sha256:{ours}"), "mediaType": media}),
        )
    }

    fn request_md(
        &self,
        row: &RequestRow,
        req: &EditRequest,
        attempt: u32,
        parent: Option<&str>,
        truncation: Option<&Value>,
        files: &[String],
    ) -> String {
        let revision = req.selection.index_revision;
        let catalog = normalize_sha256(&req.tool_catalog_revision)
            .unwrap_or_else(|| req.tool_catalog_revision.clone());
        let mut md = format!(
            "# GameCore Studio request `{id}`\n\n\
             - Change set id: `{id}` — write it as `id` in `/outputs/changeset.json`.\n\
             - Worker: `{worker}`\n\
             - Index revision: `{revision}` — cite it as `selection.indexRevision`.\n\
             - Tool catalog revision: `{catalog}`\n\
             - Attempt: {attempt}\n",
            id = row.change_set_id,
            worker = row.worker,
        );
        if let Some(p) = parent {
            md.push_str(&format!(
                "- This is a **re-ask** of task `{p}` (its parent). Its change set was rejected; \
                 the reasons are in `/inputs/diagnostics.json`. Fix them; do not repeat them.\n"
            ));
        }
        md.push_str(&format!("\n## Intent\n\n{}\n", req.intent.text.trim()));
        md.push_str("\n## Inputs (`/inputs`)\n\n");
        for f in files {
            let what = match f.as_str() {
                "request.md" => "this file",
                "selection.json" => {
                    "the selection snapshot (AuthoringRefs; never invent other ids)"
                }
                "index-slice.json" => {
                    "the bounded semantic index slice (selection closure, depth 2)"
                }
                "tool-catalog.json" => "the tool catalog: the ONLY operations you may produce",
                "diagnostics.json" => "why the previous change set was rejected",
                _ => "an attachment",
            };
            md.push_str(&format!("- `{f}` — {what}\n"));
        }
        let owner = crate::util::sha256_hex(row.app.as_bytes());
        md.push_str(&format!(
            "\n## Resource Graph queries\n\n\
             Your app/project owner namespace is `{owner}`. Use `etos query`; rows are projections, not authority. \
             Follow the selected NPC's dialogue reference in `index-slice.json` to its `dialogue.graph` assetGuid. \
             Discover that graph with `SELECT graph FROM gc_definition WHERE owner='{owner}' AND asset_guid='<assetGuid>' AND removed=false`; \
             use the returned graph value in `SELECT * FROM gc_dialogue_node WHERE owner='{owner}' AND graph='<graph>' AND removed=false`. \
             To inspect all current dialogue nodes for this project, omit the graph predicate, not the owner or removed predicates.\n"
        ));
        if let Some(t) = truncation {
            md.push_str(&format!(
                "\nThe index slice was **truncated** to fit {} bytes: {}. Ask for nothing outside it; \
                 if what you need is missing, return a clarification.\n",
                t.get("capBytes").map(Value::to_string).unwrap_or_default(),
                t.get("droppedFromEnd").map(Value::to_string).unwrap_or_default()
            ));
        }
        md.push_str(
            "\n## Output contract\n\n\
             1. Read `tool-catalog.json` first. Produce only operations whose `tool` is the `id` of \
                a tool it lists, with every required argument, no argument it does not list, a \
                target when `targetRequired` (of a kind in `targetKinds`, at a scope in `scopes`).\n\
             2. Write `/outputs/changeset.json`: `{\"id\": <change set id>, \"schema\": \
                \"gamecore.studio.changeset/1\", \"intent\", \"selection\", \"operations\", \"artifacts\", \
                \"requirements\"}` (docs/studio/schemas/change-set.schema.json: no other fields). \
                `selection` is `selection.json` as given (its `id`, `mode`, `indexRevision`, \
                `targets`). Op ids are unique; `dependsOn` names earlier ops, with no cycle.\n\
             3. Every asset you produce goes to `/outputs/` and is listed once in `artifacts[]` with \
                the `sha256` of the bytes you wrote (`sha256sum`: 64 lowercase hex digits, no \
                prefix), `name`, `mediaType`, `bytes`. Never list an asset you did not write. \
                Operations reference assets as `{\"artifact\": \"sha256:<hex>\"}`, and every listed \
                asset is referenced by an operation.\n\
             4. If two interpretations differ materially, write only `/outputs/clarification.json`: \
                `{\"status\": \"needs-clarification\", \"question\": \"<one question>\"}`.\n\
             5. Omit optional members; never write `null`. No `state` (or `\"Candidate\"`), no \
                `outcomes`, no `timestamps.applied`, no `links.gameCoreOps`.\n",
        );
        md
    }

    /// Pack and upload the first attempt's inputs.
    async fn pack(&self, row: &RequestRow) -> ApiResult<Value> {
        let req: EditRequest = serde_json::from_value(row.body.clone())
            .map_err(|e| ApiError::internal(format!("stored request unreadable: {e}")))?;
        let revision = normalize_sha256(&req.tool_catalog_revision).unwrap_or_default();
        let catalog = match &req.tool_catalog {
            Some(c) => c.clone(),
            None => self.ledger.catalog(&revision)?.ok_or_else(|| {
                ApiError::stale_context(format!("tool catalog revision {revision} is gone"))
            })?,
        };
        let (slice, truncation) = cap_slice(
            &strip_rg_extensions(&req.context_slice),
            self.cfg.max_slice_bytes,
        )
        .ok_or_else(|| {
            ApiError::new(
                axum::http::StatusCode::PAYLOAD_TOO_LARGE,
                "too_large",
                format!(
                    "the context slice cannot be truncated below {} bytes",
                    self.cfg.max_slice_bytes
                ),
            )
        })?;
        let mut names: Vec<String> = vec![
            "request.md".into(),
            "selection.json".into(),
            "index-slice.json".into(),
            "tool-catalog.json".into(),
        ];
        names.extend(req.attachments.iter().map(|a| a.name.clone()));
        let md = self.request_md(row, &req, 0, None, truncation.as_ref(), &names);
        let json_bytes = |v: &Value| serde_json::to_vec_pretty(v).unwrap_or_default();
        let mut inputs = vec![
            self.upload("request.md", "text/markdown", md.as_bytes())
                .await?,
            self.upload(
                "selection.json",
                "application/json",
                &json_bytes(row.body.get("selection").unwrap_or(&Value::Null)),
            )
            .await?,
            self.upload("index-slice.json", "application/json", &json_bytes(&slice))
                .await?,
            self.upload(
                "tool-catalog.json",
                "application/json",
                &json_bytes(&catalog),
            )
            .await?,
        ];
        for a in &req.attachments {
            let bytes = STANDARD.decode(a.data.as_bytes()).map_err(|_| {
                ApiError::bad_request(format!("attachment {} is not base64", a.name))
            })?;
            inputs.push(self.upload(&a.name, &a.media_type, &bytes).await?);
        }
        Ok(Value::Array(inputs))
    }

    /// Open the task of an attempt unless it is open already (serialised per request). A
    /// refusal etos will repeat settles the request ([`Desk::settle_refusal`]) and is
    /// returned; transient failures are returned for a retry.
    async fn open_attempt(&self, rid: &str, attempt: u32) -> ApiResult<()> {
        let lock = self.open_lock(rid);
        let _guard = lock.lock().await;
        let row = self.ledger.request(rid)?;
        let att = self.ledger.attempt(rid, attempt)?;
        if att.task_id.is_some() || row.state.is_terminal() {
            return Ok(());
        }
        let result = self.open_inner(&row, &att).await;
        match &result {
            Err(e) if is_open_timeout(e) => {
                // Not a failure: etos may still be launching the task. The next open repeats
                // the same request id, which etos answers with the task it opened (if any).
                tracing::warn!(request = rid, attempt, error = %e, "the task open timed out; re-reading it by request id");
                let _ = self.update(
                    rid,
                    RequestUpdate {
                        task_status: Some(Some("opening".into())),
                        ..RequestUpdate::default()
                    },
                );
            }
            Err(e) if !is_retryable(e) => {
                tracing::warn!(request = rid, attempt, error = %e, "task refused");
                self.settle_refusal(rid, attempt, e)?;
            }
            _ => {}
        }
        result
    }

    /// Settle a request whose task etos refused to open: the first attempt fails with the
    /// refusal; a re-ask settles `candidate_invalid` with the diagnostics that caused it plus
    /// the refusal.
    fn settle_refusal(&self, rid: &str, attempt: u32, e: &ApiError) -> Result<(), LedgerError> {
        let upd = if attempt == 0 {
            RequestUpdate {
                state: Some(RequestState::Failed),
                outcome: Some(Some(outcome_of(e, "open"))),
                ..RequestUpdate::default()
            }
        } else {
            let mut diags: Vec<Diagnostic> = self
                .ledger
                .request(rid)?
                .outcome
                .and_then(|o| o.get("diagnostics").cloned())
                .and_then(|d| serde_json::from_value(d).ok())
                .unwrap_or_default();
            diags.push(refusal_diagnostic(e, "the re-ask could not be opened"));
            RequestUpdate {
                state: Some(RequestState::CandidateInvalid),
                task_status: Some(None),
                outcome: Some(Some(json!({"code": CANDIDATE_INVALID,
                    "message": "the worker's change set failed validation and the re-ask was refused",
                    "diagnostics": diags}))),
                ..RequestUpdate::default()
            }
        };
        self.update(rid, upd).map(|_| ())
    }

    async fn open_inner(&self, row: &RequestRow, att: &AttemptRow) -> ApiResult<()> {
        let inputs = match &att.inputs {
            Some(v) => v.clone(),
            None => {
                let v = self.pack(row).await?;
                self.ledger
                    .claim_attempt_inputs(&row.request_id, att.attempt, &v)?
            }
        };
        let refs: Vec<String> = inputs
            .as_array()
            .map(|a| {
                a.iter()
                    .filter_map(|i| i.get("ref").and_then(Value::as_str).map(str::to_string))
                    .collect()
            })
            .unwrap_or_default();
        let intent = row
            .body
            .pointer("/intent/text")
            .and_then(Value::as_str)
            .unwrap_or_default();
        let short: String = intent.chars().take(300).collect();
        let text = format!(
            "GameCore Studio change set {} (attempt {}): {short}\n\nRead /inputs/request.md first. \
             Write /outputs/changeset.json (or /outputs/clarification.json).",
            row.change_set_id, att.attempt
        );
        let info = self
            .opener
            .tasks()
            .open(&TaskRequest {
                worker: row.worker.clone(),
                text,
                topic: Some(att.topic.clone()),
                inputs: refs,
                id: Some(att.etos_request_id.clone()),
            })
            .await?;
        if let Err(e) = self
            .ledger
            .set_attempt_task(&row.request_id, att.attempt, &info.task)
        {
            tracing::error!(request = %row.request_id, error = %e, "etos answered another task for the same request id");
            return Err(ApiError::ledger_conflict(e.to_string()));
        }
        tracing::info!(request = %row.request_id, attempt = att.attempt, task = %info.task, status = %info.status, "task opened");
        let current = self.ledger.request(&row.request_id)?;
        if current.state == RequestState::Cancelled {
            // Cancelled while it was being opened: cancel it at etos too.
            let _ = self.client.tasks().cancel(&info.task).await;
            return Ok(());
        }
        let state = match info.status.as_str() {
            "waiting" => RequestState::Waiting,
            _ => RequestState::Running,
        };
        self.update(
            &row.request_id,
            RequestUpdate {
                state: Some(state),
                task_status: Some(Some(info.status.clone())),
                ..RequestUpdate::default()
            },
        )?;
        Ok(())
    }

    // -----------------------------------------------------------------------------------------
    // Follow.

    /// Follow a request in the background unless it is followed already.
    pub fn spawn_follower(self: &Arc<Self>, rid: &str) {
        let Ok(mut f) = self.followers.lock() else {
            return;
        };
        if f.get(rid).is_some_and(|h| !h.is_finished()) {
            return;
        }
        let me = self.clone();
        let id = rid.to_string();
        f.insert(
            rid.to_string(),
            crate::blocking::spawn(async move { me.follow(id).await }),
        );
    }

    /// Follow every request left open by a previous process.
    pub fn resume_all(self: &Arc<Self>) -> Result<usize, LedgerError> {
        let open = self.ledger.open_requests()?;
        let mut followed = 0;
        for r in &open {
            if r.state == RequestState::Unresolved {
                let resumes = self.ledger.note_resume(&r.request_id)?;
                let age_ms = now_ms().saturating_sub(r.updated_at);
                let too_old = u128::try_from(age_ms).unwrap_or(0) > UNRESOLVED_MAX_AGE.as_millis();
                if resumes > MAX_UNRESOLVED_RESUMES || too_old {
                    let mut outcome = r.outcome.clone().unwrap_or_else(|| json!({}));
                    if let Some(o) = outcome.as_object_mut() {
                        o.insert("code".into(), json!("unresolved"));
                        o.insert("gaveUp".into(), json!(true));
                        o.insert(
                            "gaveUpReason".into(),
                            json!(format!(
                                "followed again after {} restart(s) over {} h without an outcome",
                                resumes - 1,
                                age_ms / 3_600_000
                            )),
                        );
                    }
                    if let Some((view, cursor)) = self.ledger.give_up(&r.request_id, &outcome)? {
                        self.hub.notify(cursor);
                        self.record_changeset(&view);
                    }
                    tracing::warn!(request = %r.request_id, resumes, "gave up following an unresolved request");
                    continue;
                }
            }
            tracing::info!(request = %r.request_id, state = r.state.as_str(), "resuming");
            self.spawn_follower(&r.request_id);
            followed += 1;
        }
        Ok(followed)
    }

    fn stopping(&self) -> bool {
        *self.stop.borrow()
    }

    async fn sleep_or_stop(&self, d: Duration) -> bool {
        let mut rx = self.stop.subscribe();
        tokio::select! {
            _ = tokio::time::sleep(d) => false,
            _ = rx.wait_for(|s| *s) => true,
        }
    }

    async fn follow(self: Arc<Self>, rid: String) {
        let mut backoff = Duration::from_millis(250);
        let mut unknown = 0u32;
        let mut ended_quiet = 0u32;
        let wait = Duration::from_millis(self.cfg.follow_wait_ms);
        loop {
            if self.stopping() {
                return;
            }
            let row = match self.ledger.request(&rid) {
                Ok(r) => r,
                Err(e) => {
                    tracing::error!(request = %rid, error = %e, "cannot read the request; follower stops");
                    return;
                }
            };
            if row.state.is_terminal() {
                return;
            }
            let att = match self.ledger.attempt(&rid, row.attempt) {
                Ok(a) => a,
                Err(e) => {
                    tracing::error!(request = %rid, error = %e, "cannot read the attempt; follower stops");
                    return;
                }
            };
            let Some(task) = att.task_id.clone() else {
                match self.open_attempt(&rid, row.attempt).await {
                    Ok(()) => continue,
                    Err(e) if is_open_timeout(&e) => {
                        // Give a slow launch time to land before asking again.
                        if self.sleep_or_stop(OPEN_REREAD_DELAY).await {
                            return;
                        }
                        continue;
                    }
                    Err(e) if is_retryable(&e) => {
                        tracing::warn!(request = %rid, error = %e, "cannot open the task yet");
                        if self.sleep_or_stop(backoff).await {
                            return;
                        }
                        backoff = (backoff * 2).min(Duration::from_secs(30));
                        continue;
                    }
                    Err(_) => return,
                }
            };
            let mut rx = self.stop.subscribe();
            let topics = self.client.topics();
            let page = tokio::select! {
                p = topics.read(&att.topic, att.cursor, 100, wait) => p,
                _ = rx.wait_for(|s| *s) => return,
            };
            match page {
                Ok(page) => {
                    backoff = Duration::from_millis(250);
                    let mut flow = Flow::Continue;
                    for rec in &page.records {
                        flow = self.handle_record(&row, &att, &task, rec).await;
                        if flow == Flow::Retry {
                            break;
                        }
                        if let Err(e) = self.ledger.set_attempt_cursor(&rid, att.attempt, rec.pos) {
                            tracing::error!(request = %rid, error = %e, "cannot save the topic cursor");
                        }
                        if flow != Flow::Continue {
                            break;
                        }
                    }
                    match flow {
                        Flow::Exit => return,
                        Flow::Reread => continue,
                        Flow::Retry => {
                            if self.sleep_or_stop(backoff).await {
                                return;
                            }
                            backoff = (backoff * 2).min(Duration::from_secs(30));
                            continue;
                        }
                        Flow::Continue => {}
                    }
                    if page.records.is_empty()
                        && self
                            .poll_status(&row, &task, &mut unknown, &mut ended_quiet)
                            .await
                            == Flow::Exit
                    {
                        return;
                    }
                }
                Err(e) => {
                    tracing::warn!(request = %rid, topic = %att.topic, error = %e, "topic read failed");
                    if self
                        .poll_status(&row, &task, &mut unknown, &mut ended_quiet)
                        .await
                        == Flow::Exit
                    {
                        return;
                    }
                    if self.sleep_or_stop(backoff).await {
                        return;
                    }
                    backoff = (backoff * 2).min(Duration::from_secs(30));
                }
            }
        }
    }

    /// Re-read `GET /tasks/{id}` while the topic is quiet.
    async fn poll_status(
        &self,
        row: &RequestRow,
        task: &str,
        unknown: &mut u32,
        ended_quiet: &mut u32,
    ) -> Flow {
        let info = match self.client.tasks().get(task).await {
            Ok(i) => i,
            Err(e) => {
                tracing::warn!(request = %row.request_id, task, error = %e, "task status read failed");
                return Flow::Continue;
            }
        };
        let status = info.status.clone();
        let rid = &row.request_id;
        let current = match self.ledger.request(rid) {
            Ok(r) => r,
            Err(_) => return Flow::Continue,
        };
        let changed = current.task_status.as_deref() != Some(status.as_str());
        let result = match status.as_str() {
            "cancelled" => self
                .update(
                    rid,
                    RequestUpdate {
                        state: Some(RequestState::Cancelled),
                        task_status: Some(Some(status.clone())),
                        outcome: Some(Some(json!({"code": "cancelled",
                            "message": info.error.as_deref().map(redact)}))),
                        ..RequestUpdate::default()
                    },
                )
                .map(|_| Flow::Exit),
            "unknown" => {
                *unknown += 1;
                if *unknown >= 2 {
                    self.update(
                        rid,
                        RequestUpdate {
                            state: Some(RequestState::Unresolved),
                            task_status: Some(Some(status.clone())),
                            outcome: Some(Some(json!({"code": "unresolved",
                                "message": redact(&info.error.unwrap_or_else(|| "etos has no record of the task".into()))}))),
                            ..RequestUpdate::default()
                        },
                    ).map(|_| Flow::Exit)
                } else {
                    Ok(Flow::Continue)
                }
            }
            "done" | "failed" => {
                *ended_quiet += 1;
                if *ended_quiet >= FINAL_RECORD_GRACE {
                    let (state, code) = if status == "failed" {
                        (RequestState::Failed, "task_failed")
                    } else {
                        (RequestState::Unresolved, "unresolved")
                    };
                    self.update(
                        rid,
                        RequestUpdate {
                            state: Some(state),
                            task_status: Some(Some(status.clone())),
                            outcome: Some(Some(with_model_reason(json!({"code": code,
                                "message": redact(&info.error.clone().unwrap_or_else(|| format!("etos says {status} but the final record never arrived on the topic"))),
                                "result": info.result}), &[info.error.as_deref()]))),
                            ..RequestUpdate::default()
                        },
                    ).map(|_| Flow::Exit)
                } else if changed {
                    self.update(
                        rid,
                        RequestUpdate {
                            task_status: Some(Some(status.clone())),
                            ..RequestUpdate::default()
                        },
                    )
                    .map(|_| Flow::Continue)
                } else {
                    Ok(Flow::Continue)
                }
            }
            other => {
                *unknown = 0;
                let state = if other == "waiting" {
                    RequestState::Waiting
                } else {
                    RequestState::Running
                };
                // A waiting task whose error names a degraded model says so in the outcome.
                let degraded = (state == RequestState::Waiting)
                    .then(|| info.error.as_deref().filter(|e| model_degraded(e)))
                    .flatten()
                    .map(|e| {
                        json!({"code": "waiting", "reason": "model_degraded", "text": redact(e),
                               "modelError": redact(e)})
                    });
                let new_outcome = degraded.is_some()
                    && current.outcome.as_ref().and_then(|o| o.get("reason"))
                        != Some(&json!("model_degraded"));
                if changed || current.state != state || new_outcome {
                    self.update(
                        rid,
                        RequestUpdate {
                            state: Some(state),
                            task_status: Some(Some(status.clone())),
                            outcome: degraded.map(Some),
                            ..RequestUpdate::default()
                        },
                    )
                    .map(|_| Flow::Continue)
                } else {
                    Ok(Flow::Continue)
                }
            }
        };
        result.unwrap_or_else(|e| {
            tracing::error!(request = %rid, error = %e, "ledger update failed");
            Flow::Continue
        })
    }

    async fn handle_record(
        &self,
        row: &RequestRow,
        att: &AttemptRow,
        task: &str,
        rec: &TopicRecord,
    ) -> Flow {
        let rid = &row.request_id;
        if rec.sender.starts_with("agent:") {
            return Flow::Continue;
        }
        // Settled meanwhile (a cancel through the API): nothing more to do.
        if self
            .ledger
            .request(rid)
            .is_ok_and(|r| r.state.is_terminal())
        {
            return Flow::Exit;
        }
        let text = redact(&rec.text);
        let progress = |status: &str| {
            json!({"taskId": task, "attempt": att.attempt, "pos": rec.pos, "sender": rec.sender,
                   "status": status, "text": text, "at": rec.at})
        };
        let r = match rec.status {
            None | Some(RecordStatus::Working) => {
                self.emit("task_progress", rid, progress("working"));
                if row.state == RequestState::Waiting {
                    self.update(
                        rid,
                        RequestUpdate {
                            state: Some(RequestState::Running),
                            task_status: Some(Some("running".into())),
                            ..RequestUpdate::default()
                        },
                    )
                    .map(|_| Flow::Reread)
                } else {
                    Ok(Flow::Continue)
                }
            }
            Some(RecordStatus::Waiting) => {
                self.emit("task_progress", rid, progress("waiting"));
                let reason = if text.to_ascii_lowercase().contains("budget") {
                    "budget"
                } else {
                    "waiting"
                };
                let outcome = with_model_reason(
                    json!({"code": "waiting", "reason": reason, "text": text}),
                    &[Some(&text)],
                );
                self.update(
                    rid,
                    RequestUpdate {
                        state: Some(RequestState::Waiting),
                        task_status: Some(Some("waiting".into())),
                        outcome: Some(Some(outcome)),
                        ..RequestUpdate::default()
                    },
                )
                .map(|_| Flow::Reread)
            }
            Some(RecordStatus::Failed) => {
                self.emit("task_progress", rid, progress("failed"));
                // The node closes a cancelled task with a `failed` record: ask etos which. The
                // worker's controller posts that record (refusal code `cancelled`) as soon as
                // the cancel reaches it, which can be before etos reports the task cancelled.
                let said_cancelled =
                    rec.data.pointer("/refusal/code").and_then(Value::as_str) == Some("cancelled");
                let (cancelled, task_error) = match self.client.tasks().get(task).await {
                    Ok(info) => (
                        said_cancelled || info.status == "cancelled",
                        info.error.map(|e| redact(&e)),
                    ),
                    Err(e) if e.is_retryable() => return Flow::Retry,
                    Err(_) => (said_cancelled, None),
                };
                let upd = if cancelled {
                    RequestUpdate {
                        state: Some(RequestState::Cancelled),
                        task_status: Some(Some("cancelled".into())),
                        outcome: Some(Some(json!({"code": "cancelled", "message": text}))),
                        ..RequestUpdate::default()
                    }
                } else {
                    // The record's text is the task's reason; etos's own error (when it has
                    // one and says more) is appended, and a degraded model is named.
                    let message = match &task_error {
                        Some(e) if !e.is_empty() && !text.contains(e.as_str()) => {
                            if text.is_empty() {
                                e.clone()
                            } else {
                                format!("{text} (etos: {e})")
                            }
                        }
                        _ => text.clone(),
                    };
                    RequestUpdate {
                        state: Some(RequestState::Failed),
                        task_status: Some(Some("failed".into())),
                        outcome: Some(Some(with_model_reason(
                            json!({"code": "task_failed", "message": message}),
                            &[Some(&text), task_error.as_deref()],
                        ))),
                        ..RequestUpdate::default()
                    }
                };
                self.update(rid, upd).map(|_| Flow::Exit)
            }
            Some(RecordStatus::Done) => {
                self.emit("task_progress", rid, progress("done"));
                return self.finish(row, att, task, rec).await;
            }
        };
        r.unwrap_or_else(|e| {
            tracing::error!(request = %rid, error = %e, "ledger update failed");
            Flow::Retry
        })
    }

    async fn fetch(&self, refs: &[String]) -> Result<(Vec<Fetched>, Vec<Diagnostic>), ApiError> {
        let mut files = Vec::new();
        let mut problems = Vec::new();
        for r in refs {
            match self.client.files().get(r).await {
                Ok(bytes) => {
                    let sha256 = sha256_hex(&bytes);
                    files.push(Fetched {
                        reference: r.clone(),
                        bytes,
                        sha256,
                    });
                }
                Err(e) if e.is_retryable() => return Err(e.into()),
                Err(e) => problems.push(Diagnostic::candidate(
                    "output_unreadable",
                    redact(&format!("output {r} cannot be fetched: {e}")),
                )),
            }
        }
        Ok((files, problems))
    }

    async fn finish(
        &self,
        row: &RequestRow,
        att: &AttemptRow,
        task: &str,
        rec: &TopicRecord,
    ) -> Flow {
        let rid = &row.request_id;
        let (files, problems) = match self.fetch(&rec.refs).await {
            Ok(x) => x,
            Err(e) => {
                tracing::warn!(request = %rid, error = %e, "cannot fetch outputs yet");
                return Flow::Retry;
            }
        };
        let evaluation = if problems.is_empty() {
            let revision = row
                .body
                .get("toolCatalogRevision")
                .and_then(Value::as_str)
                .and_then(normalize_sha256)
                .unwrap_or_default();
            let catalog = match self.ledger.catalog(&revision) {
                Ok(c) => c,
                Err(e) => {
                    tracing::error!(request = %rid, error = %e, "cannot read the tool catalog; retrying");
                    return Flow::Retry;
                }
            };
            evaluate(
                &files,
                &row.change_set_id,
                &self.schema,
                CatalogContext {
                    index_slice: row.body.get("contextSlice"),
                    revision: &revision,
                    catalog: catalog.as_ref(),
                },
            )
        } else {
            Evaluation::Invalid(problems)
        };
        let result = match evaluation {
            Evaluation::Valid(v) => self.accept(row, att, task, &files, *v),
            Evaluation::Clarification { question, .. } => {
                self.emit(
                    "clarification",
                    rid,
                    json!({"taskId": task, "attempt": att.attempt, "question": question}),
                );
                self.update(
                    rid,
                    RequestUpdate {
                        state: Some(RequestState::NeedsClarification),
                        task_status: Some(Some("done".into())),
                        outcome: Some(Some(
                            json!({"code": "needs_clarification", "question": question}),
                        )),
                        ..RequestUpdate::default()
                    },
                )
                .map(|_| Flow::Exit)
                .map_err(ApiError::from)
            }
            Evaluation::Invalid(diags) => {
                tracing::warn!(request = %rid, task, problems = diags.len(), "candidate invalid");
                self.emit(
                    CANDIDATE_INVALID,
                    rid,
                    json!({"taskId": task, "attempt": att.attempt, "diagnostics": diags}),
                );
                if self.cfg.reask_on_invalid && att.attempt == 0 && reaskable(&diags) {
                    match self.reask(row, att, task, &diags).await {
                        Ok(()) => Ok(Flow::Reread),
                        Err(e) if is_retryable(&e) => return Flow::Retry,
                        Err(e) => {
                            // Settled by settle_refusal when the re-ask's task was refused;
                            // otherwise (inputs could not be uploaded) settle here.
                            tracing::warn!(request = %rid, error = %e, "re-ask refused");
                            let mut all = diags.clone();
                            all.push(refusal_diagnostic(&e, "the re-ask could not be opened"));
                            self.update(
                                rid,
                                RequestUpdate {
                                    state: Some(RequestState::CandidateInvalid),
                                    task_status: Some(Some("done".into())),
                                    outcome: Some(Some(json!({"code": CANDIDATE_INVALID,
                                        "message": "the worker's change set failed validation and the re-ask was refused",
                                        "diagnostics": all}))),
                                    ..RequestUpdate::default()
                                },
                            )
                            .map(|_| Flow::Exit)
                            .map_err(ApiError::from)
                        }
                    }
                } else {
                    self.update(
                        rid,
                        RequestUpdate {
                            state: Some(RequestState::CandidateInvalid),
                            task_status: Some(Some("done".into())),
                            outcome: Some(Some(json!({"code": CANDIDATE_INVALID,
                                "message": "the worker's change set failed validation",
                                "diagnostics": diags}))),
                            ..RequestUpdate::default()
                        },
                    )
                    .map(|_| Flow::Exit)
                    .map_err(ApiError::from)
                }
            }
        };
        result.unwrap_or_else(|e| {
            tracing::error!(request = %rid, error = %e, "cannot settle the candidate; retrying");
            Flow::Retry
        })
    }

    fn accept(
        &self,
        row: &RequestRow,
        att: &AttemptRow,
        task: &str,
        files: &[Fetched],
        v: crate::candidate::ValidCandidate,
    ) -> ApiResult<Flow> {
        let rid = &row.request_id;
        let mut stored = Vec::new();
        for (entry, i) in &v.artifacts {
            let (sha, path) = self
                .store
                .put(&files[*i].bytes, Some(&entry.sha256))
                .map_err(|e| ApiError::internal(e.to_string()))?;
            let producer = entry
                .producer
                .clone()
                .unwrap_or_else(|| json!({"etosTask": task}));
            self.ledger.put_artifact(&ArtifactRow {
                sha256: sha.clone(),
                path: path.display().to_string(),
                bytes: files[*i].bytes.len() as u64,
                media_type: entry.media_type.clone(),
                name: entry.name.clone(),
                role: entry.role.clone(),
                producer: producer.clone(),
                change_set_id: Some(row.change_set_id.clone()),
            })?;
            let mut a = Map::new();
            a.insert("name".into(), json!(entry.name));
            a.insert("media_type".into(), json!(entry.media_type));
            if let Some(r) = &entry.role {
                a.insert("role".into(), json!(r));
            }
            a.insert("producer".into(), producer.clone());
            a.insert("changeset".into(), json!(row.change_set_id));
            self.indexer.record("gc_asset", &sha, a);
            stored.push(StoredArtifact {
                url: format!("/v1/artifacts/{sha}"),
                sha256: sha,
                name: entry.name.clone(),
                media_type: entry.media_type.clone(),
                bytes: files[*i].bytes.len() as u64,
                producer,
                role: entry.role.clone(),
            });
        }
        let received_at = now_ms();
        let ops = v.change_set.operations.len();
        let settled = self.ledger.accept_candidate(
            &CandidateRow {
                change_set_id: row.change_set_id.clone(),
                request_id: rid.clone(),
                task_id: task.to_string(),
                attempt: att.attempt,
                change_set: v.raw.clone(),
                artifacts: serde_json::to_value(&stored).unwrap_or(Value::Null),
                diagnostics: serde_json::to_value(&v.warnings).unwrap_or(Value::Null),
                received_at,
            },
            &RequestUpdate {
                state: Some(RequestState::Candidate),
                task_status: Some(Some("done".into())),
                outcome: Some(Some(
                    json!({"code": "candidate", "operations": ops, "artifacts": stored.len()}),
                )),
                ..RequestUpdate::default()
            },
        )?;
        let Some((view, cursor)) = settled else {
            tracing::info!(request = %rid, task, "request already settled; candidate not recorded");
            return Ok(Flow::Exit);
        };
        self.hub.notify(cursor);
        self.record_changeset(&view);
        let mut cs = Map::new();
        cs.insert("state".into(), json!("candidate"));
        cs.insert("intent".into(), json!(v.change_set.intent.text));
        cs.insert("ops_count".into(), json!(ops));
        cs.insert("task".into(), json!(task));
        self.indexer.record("gc_changeset", &row.change_set_id, cs);
        tracing::info!(request = %rid, task, operations = ops, artifacts = stored.len(), "candidate accepted");
        self.emit(
            "candidate",
            rid,
            json!({"changeSetId": row.change_set_id, "taskId": task, "attempt": att.attempt,
                   "operations": ops, "artifacts": stored, "warnings": v.warnings,
                   "url": format!("/v1/candidates/{}", row.change_set_id)}),
        );
        Ok(Flow::Exit)
    }

    async fn reask(
        &self,
        row: &RequestRow,
        att: &AttemptRow,
        task: &str,
        diags: &[Diagnostic],
    ) -> ApiResult<()> {
        let n = att.attempt + 1;
        let req: EditRequest = serde_json::from_value(row.body.clone())
            .map_err(|e| ApiError::internal(format!("stored request unreadable: {e}")))?;
        let previous: Vec<Value> = att
            .inputs
            .as_ref()
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default()
            .into_iter()
            .filter(|i| {
                !matches!(
                    i.get("name").and_then(Value::as_str),
                    Some("request.md" | "diagnostics.json")
                )
            })
            .collect();
        let mut names = vec!["request.md".to_string()];
        names.extend(
            previous
                .iter()
                .filter_map(|i| i.get("name").and_then(Value::as_str).map(str::to_string)),
        );
        names.push("diagnostics.json".into());
        let md = self.request_md(row, &req, n, Some(task), None, &names);
        let diag_json =
            serde_json::to_vec_pretty(&json!({"parentTask": task, "diagnostics": diags}))
                .unwrap_or_default();
        let mut inputs = vec![
            self.upload("request.md", "text/markdown", md.as_bytes())
                .await?,
        ];
        inputs.extend(previous);
        inputs.push(
            self.upload("diagnostics.json", "application/json", &diag_json)
                .await?,
        );
        let begun = self.ledger.begin_reask(
            &AttemptRow {
                request_id: row.request_id.clone(),
                attempt: n,
                etos_request_id: format!("{}.r{n}", row.change_set_id),
                topic: self.topic(&row.change_set_id, n),
                task_id: None,
                parent_task: Some(task.to_string()),
                cursor: 0,
                inputs: Some(Value::Array(inputs)),
                created_at: now_ms(),
            },
            &RequestUpdate {
                state: Some(RequestState::Running),
                attempt: Some(n),
                task_status: Some(None),
                outcome: Some(Some(json!({"code": CANDIDATE_INVALID, "reask": true,
                    "parentTask": task, "diagnostics": diags}))),
            },
        )?;
        let Some((view, cursor)) = begun else {
            return Ok(());
        };
        self.hub.notify(cursor);
        self.record_changeset(&view);
        tracing::info!(request = %row.request_id, parent = task, attempt = n, "re-asking once");
        match self.open_attempt(&row.request_id, n).await {
            Ok(()) => Ok(()),
            // The follower opens it when etos is reachable again.
            Err(e) if is_retryable(&e) => Ok(()),
            Err(e) => Err(e),
        }
    }

    // -----------------------------------------------------------------------------------------
    // Cancel and shutdown.

    /// `POST /v1/requests/{id}/cancel`.
    pub async fn cancel(self: &Arc<Self>, rid: &str, app: &str) -> ApiResult<RequestView> {
        let row = self.ledger.request(rid).map_err(|e| match e {
            LedgerError::NotFound(_) => ApiError::not_found(format!("no request {rid}")),
            other => other.into(),
        })?;
        if row.app != app {
            return Err(ApiError::not_found(format!("no request {rid}")));
        }
        if row.state.is_terminal() {
            return Ok(self.ledger.request_view(rid)?);
        }
        let att = self.ledger.attempt(rid, row.attempt)?;
        let Some(task) = att.task_id else {
            return Ok(self.update(
                rid,
                RequestUpdate {
                    state: Some(RequestState::Cancelled),
                    outcome: Some(Some(json!({"code": "cancelled", "message": "cancelled before the task was opened"}))),
                    ..RequestUpdate::default()
                },
            )?);
        };
        let info = self.client.tasks().cancel(&task).await?;
        tracing::info!(request = rid, task = %task, status = %info.status, "cancel requested");
        let mut upd = RequestUpdate {
            task_status: Some(Some(info.status.clone())),
            ..RequestUpdate::default()
        };
        if info.status == "cancelled" {
            upd.state = Some(RequestState::Cancelled);
            upd.outcome = Some(Some(
                json!({"code": "cancelled", "message": info.error.as_deref().map(redact)}),
            ));
        }
        let view = self.update(rid, upd)?;
        if !view.state.is_terminal() {
            self.spawn_follower(rid);
        }
        Ok(view)
    }

    /// Stop the followers (they resume from their cursors next start).
    pub async fn shutdown(&self) {
        let _ = self.stop.send(true);
        let handles: Vec<JoinHandle<()>> = match self.followers.lock() {
            Ok(mut f) => f.drain().map(|(_, h)| h).collect(),
            Err(_) => Vec::new(),
        };
        for h in handles {
            let abort = h.abort_handle();
            if tokio::time::timeout(Duration::from_secs(3), h)
                .await
                .is_err()
            {
                abort.abort();
            }
        }
    }

    /// Requests followed right now.
    pub fn following(&self) -> usize {
        self.followers
            .lock()
            .map(|f| f.values().filter(|h| !h.is_finished()).count())
            .unwrap_or(0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn degraded_models_are_named_in_outcomes() {
        let o = with_model_reason(
            json!({"code": "task_failed", "message": "x"}),
            &[
                Some("the turn failed"),
                Some("model marked degraded model=echo/claude-opus-5-5"),
            ],
        );
        assert_eq!(o["reason"], "model_degraded");
        assert!(
            o["modelError"]
                .as_str()
                .unwrap_or_default()
                .contains("degraded")
        );
        assert!(model_degraded(
            "HTTP 503: auth_unavailable: no auth available (providers=claude, model=claude-opus-5-5)"
        ));
        assert!(model_degraded(
            "HTTP 401 OAuth access token has been revoked."
        ));
        let plain = with_model_reason(
            json!({"code": "task_failed"}),
            &[Some("tests failed"), None],
        );
        assert!(plain.get("reason").is_none());
        let t = ApiError::new(
            axum::http::StatusCode::BAD_GATEWAY,
            "transport",
            "POST /tasks timed out after 180000 ms",
        );
        assert!(is_open_timeout(&t) && is_retryable(&t));
        let down = ApiError::new(
            axum::http::StatusCode::BAD_GATEWAY,
            "transport",
            "cannot reach the node",
        );
        assert!(!is_open_timeout(&down));
    }

    #[test]
    fn body_limit_covers_the_attachment_budget() {
        let b64 = |n: usize| 4 * n.div_ceil(3);
        assert!(MAX_BODY >= b64(MAX_ATTACHMENTS_TOTAL) + MAX_JSON_BODY);
        const { assert!(MAX_ATTACHMENTS_TOTAL >= MAX_ATTACHMENT) };
        const { assert!(MAX_ATTACHMENTS * MAX_ATTACHMENT >= MAX_ATTACHMENTS_TOTAL) };
    }

    #[test]
    fn digest_ignores_the_catalog_and_attachment_encoding() {
        let data = STANDARD.encode(b"frame");
        let a = json!({"changeSetId": "c", "toolCatalogRevision": "r",
                       "attachments": [{"name": "f.png", "mediaType": "image/png", "data": data}]});
        let mut b = a.clone();
        b["toolCatalog"] = json!({"tools": []});
        b["attachments"][0]["sha256"] = json!(sha256_hex(b"frame"));
        assert_eq!(request_digest(&a), request_digest(&b));
        let mut c = a.clone();
        c["attachments"][0]["data"] = json!(STANDARD.encode(b"other"));
        assert_ne!(request_digest(&a), request_digest(&c));
    }

    #[test]
    fn rg_extensions_are_stripped() {
        let v = strip_rg_extensions(
            &json!({"nodes": [{"ref": {}, "type": "t", "rgKind": "gc_quest", "rgKey": "q"}]}),
        );
        assert_eq!(v, json!({"nodes": [{"ref": {}, "type": "t"}]}));
    }

    #[test]
    fn slices_are_capped_from_the_end() {
        let nodes: Vec<Value> = (0..100)
            .map(|i| json!({"n": i, "pad": "x".repeat(100)}))
            .collect();
        let slice = json!({"revision": 3, "nodes": nodes, "edges": []});
        let (same, none) = cap_slice(&slice, 1 << 20).unwrap();
        assert_eq!(same, slice);
        assert!(none.is_none());
        let (cut, info) = cap_slice(&slice, 2000).unwrap();
        let info = info.unwrap();
        assert!(info["droppedFromEnd"]["nodes"].as_u64().unwrap() > 80);
        assert_eq!(cut["nodes"][0]["n"], 0);
        assert!(cut["truncated"].is_object());
        assert!(cap_slice(&json!({"blob": "y".repeat(5000)}), 100).is_none());
    }
}
