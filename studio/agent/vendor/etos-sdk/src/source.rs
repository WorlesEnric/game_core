//! App sources: register a source, then answer the node's structured read requests with a
//! handler. The SDK checks each request against the declared capabilities before the handler
//! sees it and adds the scope condition to the filter, so a handler that applies the filter
//! enforces the user partition.

use std::future::Future;
use std::sync::Arc;
use std::time::Duration;

use futures::FutureExt;
use futures::future::BoxFuture;
use reqwest::Method;
use serde_json::{Map, Value};
use tokio::sync::{Semaphore, watch};

use crate::client::Client;
use crate::error::{Error, Refusal, Result};
use crate::util::{now_ms, seg};
use crate::wire::{
    FieldType, Filter, FilterLeaf, FilterOp, Pagination, ScopeRule, SourceAck, SourceRegistration,
    SourceRequestBatch, SourceResponseAck, StructuredRequest, StructuredResponse,
};

/// What a source handler answers.
#[derive(Debug, Clone, Default)]
pub struct SourceAnswer {
    /// The rows, each holding at least the collection's key fields.
    pub rows: Vec<Map<String, Value>>,
    /// The next page's cursor.
    pub cursor: Option<String>,
    /// False when the source knows the answer is partial.
    pub complete: bool,
}

type Handler = Arc<
    dyn Fn(StructuredRequest) -> BoxFuture<'static, std::result::Result<SourceAnswer, Refusal>>
        + Send
        + Sync,
>;

/// A registered source being served. [`SourceHandle::stop`] stops it and waits for the answers
/// in flight; dropping the handle stops it without waiting.
#[derive(Debug)]
pub struct SourceHandle {
    /// The source id.
    pub id: String,
    /// The registration version the node assigned.
    pub version: u64,
    stop: watch::Sender<bool>,
    task: tokio::task::JoinHandle<()>,
}

impl SourceHandle {
    /// Stop answering requests (requests in flight are finished).
    pub async fn stop(self) {
        let _ = self.stop.send(true);
        let _ = self.task.await;
    }
}

impl Client {
    /// Register the app source `id` and answer its requests with `handler` until the handle is
    /// stopped. At most `concurrency` requests run at once.
    pub async fn register_source<F, Fut>(
        &self,
        id: &str,
        registration: SourceRegistration,
        concurrency: usize,
        handler: F,
    ) -> Result<SourceHandle>
    where
        F: Fn(StructuredRequest) -> Fut + Send + Sync + 'static,
        Fut: Future<Output = std::result::Result<SourceAnswer, Refusal>> + Send + 'static,
    {
        check_registration(&registration)?;
        let path = format!("/sources/{}", seg(id));
        let ack: SourceAck = self
            .json(
                Method::PUT,
                &path,
                Some(&registration),
                true,
                Duration::ZERO,
            )
            .await?;
        let handler: Handler = Arc::new(move |r| handler(r).boxed());
        let (stop, stopped) = watch::channel(false);
        let task = tokio::spawn(serve(
            self.clone(),
            path,
            Arc::new(registration),
            handler,
            concurrency.max(1),
            stopped,
        ));
        Ok(SourceHandle {
            id: ack.id,
            version: ack.version,
            stop,
            task,
        })
    }
}

async fn serve(
    client: Client,
    path: String,
    reg: Arc<SourceRegistration>,
    handler: Handler,
    concurrency: usize,
    mut stopped: watch::Receiver<bool>,
) {
    let wait = Duration::from_secs(25);
    let slots = Arc::new(Semaphore::new(concurrency));
    let mut failures: u32 = 0;
    loop {
        if *stopped.borrow() {
            break;
        }
        let requests = format!("{path}/requests?wait_ms={}", wait.as_millis());
        let poll =
            client.json::<SourceRequestBatch>(Method::GET, &requests, None::<&Value>, true, wait);
        let batch = tokio::select! {
            _ = stopped.changed() => break,
            b = poll => b,
        };
        let batch = match batch {
            Ok(b) => {
                failures = 0;
                b
            }
            Err(e) => {
                failures = failures.saturating_add(1);
                tracing::warn!(source = %path, error = %e, "etos source: polling failed");
                let delay = Duration::from_millis(200 * 2u64.pow(failures.min(8)));
                tokio::select! {
                    _ = stopped.changed() => break,
                    _ = tokio::time::sleep(delay.min(Duration::from_secs(30))) => {}
                }
                continue;
            }
        };
        for request in batch.requests {
            let Ok(permit) = slots.clone().acquire_owned().await else {
                return;
            };
            let (client, path, reg, handler) =
                (client.clone(), path.clone(), reg.clone(), handler.clone());
            tokio::spawn(async move {
                let response = answer(&reg, &handler, request).await;
                let sent: Result<SourceResponseAck> = client
                    .json(
                        Method::POST,
                        &format!("{path}/responses"),
                        Some(&response),
                        true,
                        Duration::ZERO,
                    )
                    .await;
                if let Err(e) = sent {
                    tracing::warn!(source = %path, error = %e, "etos source: answer not delivered");
                }
                drop(permit);
            });
        }
    }
    // Wait for the answers in flight.
    let _ = slots.acquire_many(concurrency as u32).await;
}

async fn answer(
    reg: &SourceRegistration,
    handler: &Handler,
    request: StructuredRequest,
) -> StructuredResponse {
    let id = request.request.clone();
    let refused = |r: Refusal| StructuredResponse {
        request: id.clone(),
        rows: Vec::new(),
        cursor: None,
        complete: false,
        read_at: now_ms(),
        refusal: Some(r),
    };
    let request = match check_request(reg, request) {
        Ok(r) => r,
        Err(message) => return refused(Refusal::new("invalid_request", message)),
    };
    let key: Vec<String> = reg
        .schema
        .collections
        .get(&request.collection)
        .map(|c| c.key.clone())
        .unwrap_or_default();
    let limit = request.limit as usize;
    match handler(request).await {
        Ok(a) => {
            if a.rows.len() > limit {
                return refused(Refusal::new(
                    "handler_error",
                    format!(
                        "the handler returned {} rows for a limit of {limit}",
                        a.rows.len()
                    ),
                ));
            }
            if let Some(i) = a
                .rows
                .iter()
                .position(|row| key.iter().any(|k| !row.contains_key(k)))
            {
                return refused(Refusal::new(
                    "handler_error",
                    format!("row {i} lacks a key field ({})", key.join(", ")),
                ));
            }
            StructuredResponse {
                request: id.clone(),
                rows: a.rows,
                cursor: a.cursor,
                complete: a.complete,
                read_at: now_ms(),
                refusal: None,
            }
        }
        Err(r) => refused(r),
    }
}

fn check_registration(reg: &SourceRegistration) -> Result<()> {
    let bad = |m: String| Err(Error::Invalid(m));
    if reg.schema.collections.is_empty() {
        return bad("the schema declares no collections".into());
    }
    for (name, c) in &reg.schema.collections {
        if c.key.is_empty() || c.key.iter().any(|k| !c.fields.contains_key(k)) {
            return bad(format!("collection {name:?} must name its key fields"));
        }
        let Some(caps) = reg.capabilities.collections.get(name) else {
            return bad(format!("collection {name:?} has no capabilities"));
        };
        if let Some(f) = caps.fields.keys().find(|f| !c.fields.contains_key(*f)) {
            return bad(format!("capabilities of {name:?} name unknown field {f:?}"));
        }
        if caps.max_page == 0 {
            return bad(format!("max_page of {name:?} must be positive"));
        }
        match reg.scope.get(name) {
            None => return bad(format!("collection {name:?} has no scope rule")),
            Some(ScopeRule::User { field }) if !c.fields.contains_key(field) => {
                return bad(format!(
                    "the scope rule of {name:?} names unknown field {field:?}"
                ));
            }
            Some(_) => {}
        }
    }
    if let Some(name) = reg
        .capabilities
        .collections
        .keys()
        .find(|n| !reg.schema.collections.contains_key(*n))
    {
        return bad(format!("capabilities name unknown collection {name:?}"));
    }
    Ok(())
}

const MAX_FILTER_NODES: usize = 256;
const MAX_FILTER_DEPTH: usize = 16;
const MAX_IN: usize = 1000;
const MAX_SORT: usize = 8;

/// Check a request against the registration and add the scope condition to its filter.
/// The error is the message of an `invalid_request` refusal.
pub(crate) fn check_request(
    reg: &SourceRegistration,
    mut req: StructuredRequest,
) -> std::result::Result<StructuredRequest, String> {
    let (Some(schema), Some(caps)) = (
        reg.schema.collections.get(&req.collection),
        reg.capabilities.collections.get(&req.collection),
    ) else {
        return Err(format!("unknown collection {:?}", req.collection));
    };
    let mut seen = std::collections::BTreeSet::new();
    for f in &req.projection {
        if !schema.fields.contains_key(f) {
            return Err(format!("projection names unknown field {f:?}"));
        }
        if !seen.insert(f) {
            return Err(format!("projection repeats field {f:?}"));
        }
    }
    if let Some(filter) = &req.filter {
        let mut nodes = 0;
        walk(filter, 1, &mut nodes, &|leaf: &FilterLeaf| {
            let (Some(cap), Some(field)) =
                (caps.fields.get(&leaf.field), schema.fields.get(&leaf.field))
            else {
                return Err(format!("field {:?} cannot be filtered", leaf.field));
            };
            if !cap.ops.contains(&leaf.op) {
                return Err(format!(
                    "field {:?} does not support {:?}",
                    leaf.field, leaf.op
                ));
            }
            let fits = match leaf.op {
                FilterOp::In => leaf.value.as_array().is_some_and(|a| {
                    !a.is_empty() && a.len() <= MAX_IN && a.iter().all(|x| scalar_fits(field.ty, x))
                }),
                FilterOp::IsNull => leaf.value.is_boolean(),
                FilterOp::Prefix | FilterOp::Match => leaf.value.is_string(),
                _ => scalar_fits(field.ty, &leaf.value),
            };
            if fits {
                Ok(())
            } else {
                Err(format!(
                    "the value of {:?} {:?} does not fit the field's type",
                    leaf.field, leaf.op
                ))
            }
        })?;
    }
    if req.sort.len() > MAX_SORT {
        return Err(format!("sort takes at most {MAX_SORT} keys"));
    }
    if let Some(s) = req
        .sort
        .iter()
        .find(|s| !caps.fields.get(&s.field).is_some_and(|c| c.sortable))
    {
        return Err(format!("field {:?} is not sortable", s.field));
    }
    if req.limit == 0 || req.limit > caps.max_page {
        return Err(format!("limit must be between 1 and {}", caps.max_page));
    }
    if req.cursor.is_some() && caps.pagination == Pagination::None {
        return Err(format!("collection {:?} does not page", req.collection));
    }
    for (name, v) in &req.params {
        match caps.params.get(name) {
            None => return Err(format!("unknown parameter {name:?}")),
            Some(p) if !scalar_fits(p.ty, v) => {
                return Err(format!("parameter {name:?} has the wrong type"));
            }
            Some(_) => {}
        }
    }
    if let Some((name, _)) = caps
        .params
        .iter()
        .find(|(n, p)| p.required && !req.params.contains_key(*n))
    {
        return Err(format!("parameter {name:?} is required"));
    }
    if let (Some(ScopeRule::User { field }), Some(user)) =
        (reg.scope.get(&req.collection), req.scope.user.clone())
    {
        let cond = Filter::Leaf(FilterLeaf {
            field: field.clone(),
            op: FilterOp::Eq,
            value: Value::String(user),
        });
        req.filter = Some(match req.filter.take() {
            None => cond,
            Some(f) => Filter::And { and: vec![cond, f] },
        });
    }
    Ok(req)
}

fn walk(
    f: &Filter,
    depth: usize,
    nodes: &mut usize,
    leaf: &dyn Fn(&FilterLeaf) -> std::result::Result<(), String>,
) -> std::result::Result<(), String> {
    *nodes += 1;
    if *nodes > MAX_FILTER_NODES {
        return Err(format!("the filter has more than {MAX_FILTER_NODES} nodes"));
    }
    if depth > MAX_FILTER_DEPTH {
        return Err(format!(
            "the filter is nested deeper than {MAX_FILTER_DEPTH}"
        ));
    }
    match f {
        Filter::And { and: children } | Filter::Or { or: children } => children
            .iter()
            .try_for_each(|c| walk(c, depth + 1, nodes, leaf)),
        Filter::Not { not } => walk(not, depth + 1, nodes, leaf),
        Filter::Leaf(l) => leaf(l),
    }
}

fn scalar_fits(ty: FieldType, v: &Value) -> bool {
    match ty {
        FieldType::String => v.is_string(),
        FieldType::Int => v.is_i64() || v.is_u64(),
        FieldType::Number => v.is_number(),
        FieldType::Bool => v.is_boolean(),
        FieldType::Time => v.is_string() || v.is_i64(),
        FieldType::Json => v.is_string() || v.is_boolean() || v.is_number(),
    }
}
