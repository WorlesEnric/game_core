//! Resource Graph publication (04 §7): the companion is the logger of its own binding
//! (`PUT /bindings/gamecore-studio`, digest computed by the SDK). States are declared once,
//! all global (the index is per project, not per user) and mirrored:
//!
//! | Kind | Key | Props | Links |
//! |---|---|---|---|
//! | `gc_project` | `project` | name, unity, kernel_tag, index_revision | |
//! | `gc_entity` | `authoring_id` | name, type, fields, removed | region, definition |
//! | `gc_definition` | `definition` (name@revision) | type, fields, asset_path, removed | |
//! | `gc_dialogue_node` | `node` (graph/node) | graph, text, conditions, consequences, removed | speaker |
//! | `gc_quest` | `quest` (definition name) | name, stages, status_schema, removed | |
//! | `gc_rule` | `rule` (definition name) | trigger, conditions, actions, removed | |
//! | `gc_region` | `region` | name, scene, portals, removed | |
//! | `gc_changeset` | `change_set` | state, intent, ops_count, outcome | task |
//! | `gc_task` | `task` | worker, status, usage | changeset |
//! | `gc_asset` | `sha256` | name, media_type, role, producer | changeset |
//!
//! (`region`/`definition`/`speaker` are links only: the SDK refuses a name that is both a
//! property and a link.) A removal is logged as `removed: true` with op `remove` (the logger
//! has no delete). Changes are coalesced per (kind, key) and handed to the SDK logger by one
//! ticker, at most one delivery round per `index_flush_ms`; [`Indexer::close`] flushes on
//! shutdown.

use std::collections::{BTreeMap, BTreeSet};
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use etos_sdk::wire::{PropSpec, PropType, StateSpec};
use etos_sdk::{ChangeMeta, Client, FileStore, Logger, LoggerOptions};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value, json};
use tokio::sync::Notify;

use crate::model::{AuthoringRef, IndexDelta, IndexDeltaAck, IndexNode, RefKind};
use crate::util::now_ms;

/// Most coalesced rows waiting for delivery.
const MAX_PENDING: usize = 200_000;

/// The declared states.
pub fn states() -> BTreeMap<String, StateSpec> {
    let removed = |s: StateSpec| s.prop("removed", PropType::Bool);
    let text = |d: &str| PropSpec {
        ty: PropType::Text,
        values: None,
        description: Some(d.to_string()),
    };
    let mut m = BTreeMap::new();
    let g = |k: &str| StateSpec::new(k).global();
    m.insert(
        "gc_project".into(),
        g("project")
            .prop("name", PropType::String)
            .prop("unity", PropType::String)
            .prop("kernel_tag", PropType::String)
            .prop("index_revision", PropType::Int),
    );
    m.insert(
        "gc_entity".into(),
        removed(
            g("authoring_id")
                .prop("name", PropType::String)
                .prop("type", PropType::String)
                .prop("fields", PropType::Json)
                .link("region", "gc_region")
                .link("definition", "gc_definition"),
        ),
    );
    m.insert(
        "gc_definition".into(),
        removed(
            g("definition")
                .prop("type", PropType::String)
                .prop("fields", PropType::Json)
                .prop("name", PropType::String)
                .prop("owner", PropType::String)
                .prop("graph", PropType::String)
                .prop("asset_guid", PropType::String)
                .prop("authoring_id", PropType::String)
                .prop("asset_path", PropType::String),
        ),
    );
    m.insert(
        "gc_dialogue_node".into(),
        removed(
            g("node")
                .prop("graph", PropType::String)
                .prop("owner", PropType::String)
                .prop("node_index", PropType::Int)
                .prop("kind", PropType::String)
                .prop("speaker_name", PropType::String)
                .prop("options", PropType::Json)
                .prop("edges", PropType::Json)
                .prop("text", text("the line"))
                .prop("conditions", PropType::Json)
                .prop("consequences", PropType::Json)
                .link("speaker", "gc_entity"),
        ),
    );
    m.insert(
        "gc_quest".into(),
        removed(
            g("quest")
                .prop("name", PropType::String)
                .prop("stages", PropType::Json)
                .prop("status_schema", PropType::Json),
        ),
    );
    m.insert(
        "gc_rule".into(),
        removed(
            g("rule")
                .prop("trigger", PropType::Json)
                .prop("conditions", PropType::Json)
                .prop("actions", PropType::Json),
        ),
    );
    m.insert(
        "gc_region".into(),
        removed(
            g("region")
                .prop("name", PropType::String)
                .prop("scene", PropType::String)
                .prop("portals", PropType::Json),
        ),
    );
    m.insert(
        "gc_changeset".into(),
        g("change_set")
            .prop("state", PropType::String)
            .prop("intent", text("the prompt"))
            .prop("ops_count", PropType::Int)
            .prop("outcome", PropType::Json)
            .link("task", "gc_task"),
    );
    m.insert(
        "gc_task".into(),
        g("task")
            .prop("worker", PropType::String)
            .prop("status", PropType::String)
            .prop("usage", PropType::Json)
            .link("changeset", "gc_changeset"),
    );
    m.insert(
        "gc_asset".into(),
        g("sha256")
            .prop("name", PropType::String)
            .prop("media_type", PropType::String)
            .prop("role", PropType::String)
            .prop("producer", PropType::Json)
            .link("changeset", "gc_changeset"),
    );
    m
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
struct Pending {
    values: Map<String, Value>,
    op: String,
    correlation: Option<String>,
}

type ResourceRowKey = (String, String);
type SourceRows = BTreeMap<String, BTreeSet<ResourceRowKey>>;
type OwnerInventory = BTreeMap<String, SourceRows>;

#[derive(Default)]
struct Queue {
    rows: BTreeMap<ResourceRowKey, Pending>,
    revision: Option<u64>,
    sources: OwnerInventory,
    storage_error: Option<String>,
}

#[derive(Default, Serialize, Deserialize)]
struct SavedQueue {
    rows: Vec<(String, String, Pending)>,
    sources: OwnerInventory,
    revision: Option<u64>,
}

/// The index logger.
pub struct Indexer {
    client: Client,
    app: String,
    store: PathBuf,
    inventory: PathBuf,
    every: Duration,
    queue: Mutex<Queue>,
    logger: tokio::sync::Mutex<Option<Logger>>,
    wake: Notify,
    closed: std::sync::atomic::AtomicBool,
}

impl std::fmt::Debug for Indexer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Indexer")
            .field("app", &self.app)
            .finish_non_exhaustive()
    }
}

/// Why an index delta was refused.
#[derive(Debug, thiserror::Error)]
pub enum IndexError {
    /// Too much is waiting for delivery (the node is unreachable or the binding refused).
    #[error("backpressure: {0} index rows are waiting for delivery")]
    Backpressure(usize),
    /// The queue is unusable.
    #[error("the index queue is poisoned")]
    Poisoned,
    /// The publication inventory/outbox could not be read or durably written.
    #[error("index publication storage: {0}")]
    Storage(String),
}

fn field_value(node: &IndexNode, names: &[&str]) -> Option<Value> {
    names
        .iter()
        .find_map(|n| node.fields.get(*n).and_then(|f| f.value.clone()))
}

fn link_to(node: &IndexNode, field: &str) -> Option<String> {
    node.refs.iter().find(|r| r.field == field).and_then(|r| {
        r.to.authoring_id
            .clone()
            .or_else(|| r.to.definition.clone())
    })
}

fn kind_of(node: &IndexNode) -> Option<&'static str> {
    if let Some(k) = &node.rg_kind {
        return match k.as_str() {
            "gc_entity" => Some("gc_entity"),
            "gc_definition" => Some("gc_definition"),
            "gc_dialogue_node" => Some("gc_dialogue_node"),
            "gc_quest" => Some("gc_quest"),
            "gc_rule" => Some("gc_rule"),
            "gc_region" => Some("gc_region"),
            _ => None,
        };
    }
    let ty = node.ty.to_ascii_lowercase();
    match node.reference.kind {
        RefKind::Entity => Some("gc_entity"),
        RefKind::Region => Some("gc_region"),
        RefKind::Definition => Some(if ty.starts_with("dialogue.") && ty.contains("node") {
            "gc_dialogue_node"
        } else if ty.starts_with("quest.") {
            "gc_quest"
        } else if ty.starts_with("logic.") || ty.contains("rule") {
            "gc_rule"
        } else {
            "gc_definition"
        }),
        _ => None,
    }
}

fn key_of(kind: &str, r: &AuthoringRef, name: &str) -> Option<String> {
    let id = r.authoring_id.clone();
    match kind {
        "gc_definition" => r.definition.clone().or(id),
        // A quest or rule is keyed by its definition name, stable across revisions.
        "gc_quest" | "gc_rule" => r
            .definition
            .as_deref()
            .map(|d| d.split('@').next().unwrap_or(d).to_string())
            .or(id),
        "gc_region" => id.or_else(|| (!name.is_empty()).then(|| name.to_string())),
        _ => id.or_else(|| r.definition.clone()),
    }
    .filter(|k| !k.is_empty() && k.len() <= 512)
}

/// The Resource Graph row of an index node: `(kind, key, values)`, or `None` for things the
/// graph does not hold (scene objects, assets, UI elements, scopes, locations).
pub fn node_row(node: &IndexNode) -> Option<(&'static str, String, Map<String, Value>)> {
    let kind = kind_of(node)?;
    let key = node
        .rg_key
        .clone()
        .or_else(|| key_of(kind, &node.reference, &node.name))?;
    let fields = || serde_json::to_value(&node.fields).unwrap_or(Value::Null);
    let mut v = Map::new();
    let mut put = |k: &str, val: Option<Value>| {
        if let Some(val) = val {
            v.insert(k.to_string(), val);
        }
    };
    match kind {
        "gc_entity" => {
            put("name", Some(json!(node.name)));
            put("type", Some(json!(node.ty)));
            put("fields", Some(fields()));
            let region = link_to(node, "region")
                .or_else(|| node.reference.location.as_ref().map(|l| l.region.clone()));
            put("region", region.map(Value::String));
            put(
                "definition",
                node.reference.definition.clone().map(Value::String),
            );
        }
        "gc_definition" => {
            put("type", Some(json!(node.ty)));
            put("name", Some(json!(node.name)));
            put(
                "asset_guid",
                node.reference.asset_guid.clone().map(Value::String),
            );
            put(
                "authoring_id",
                node.reference.authoring_id.clone().map(Value::String),
            );
            if node.ty == "dialogue.graph" {
                put("graph", graph_key(&node.reference).map(Value::String));
            }
            put("fields", Some(fields()));
            put(
                "asset_path",
                node.provenance
                    .as_ref()
                    .and_then(|p| p.get("asset"))
                    .and_then(Value::as_str)
                    .map(|s| json!(s)),
            );
        }
        "gc_dialogue_node" => {
            put(
                "graph",
                field_value(node, &["graph"]).map(|g| match g {
                    Value::String(s) => Value::String(s),
                    other => Value::String(other.to_string()),
                }),
            );
            put(
                "text",
                field_value(node, &["text", "line"]).map(|t| match t {
                    Value::String(s) => Value::String(s),
                    other => Value::String(other.to_string()),
                }),
            );
            put("conditions", field_value(node, &["conditions"]));
            put("consequences", field_value(node, &["consequences"]));
            put("speaker", link_to(node, "speaker").map(Value::String));
        }
        "gc_quest" => {
            put("name", Some(json!(node.name)));
            put("stages", field_value(node, &["stages"]));
            put(
                "status_schema",
                field_value(node, &["statusSchema", "status_schema"]),
            );
        }
        "gc_rule" => {
            put("trigger", field_value(node, &["trigger"]));
            put("conditions", field_value(node, &["conditions"]));
            put("actions", field_value(node, &["actions"]));
        }
        "gc_region" => {
            put("name", Some(json!(node.name)));
            put(
                "scene",
                field_value(node, &["scene"])
                    .and_then(|s| s.as_str().map(|x| json!(x)))
                    .or_else(|| node.reference.path.clone().map(Value::String)),
            );
            put("portals", field_value(node, &["portals"]));
        }
        _ => return None,
    }
    v.insert("removed".into(), Value::Bool(false));
    Some((kind, key, v))
}

fn graph_key(reference: &AuthoringRef) -> Option<String> {
    reference
        .asset_guid
        .clone()
        .or_else(|| reference.authoring_id.clone())
        .or_else(|| {
            reference
                .definition
                .as_deref()
                .map(|d| d.split('@').next().unwrap_or(d).to_string())
        })
}

fn source_key(reference: &AuthoringRef) -> String {
    let identity = reference
        .authoring_id
        .as_ref()
        .or(reference.global.as_ref())
        .or(reference.asset_guid.as_ref())
        .or(reference.path.as_ref());
    format!(
        "{:?}:{}",
        reference.kind,
        identity.map(String::as_str).unwrap_or_else(|| {
            reference
                .definition
                .as_deref()
                .unwrap_or("")
                .split('@')
                .next()
                .unwrap_or("")
        })
    )
}

/// Dialogue nodes have no serialized ids: edges address their zero-based list indices.
/// Only actual object entries in fields.nodes.value produce rows; no entry/end nodes are invented.
fn dialogue_rows(node: &IndexNode) -> Vec<(String, Map<String, Value>)> {
    if node.ty != "dialogue.graph" {
        return Vec::new();
    }
    let Some(graph) = graph_key(&node.reference) else {
        return Vec::new();
    };
    let Some(nodes) = node
        .fields
        .get("nodes")
        .and_then(|f| f.value.as_ref())
        .and_then(Value::as_array)
    else {
        return Vec::new();
    };
    let graph_speaker = field_value(node, &["speaker"]).unwrap_or(json!(""));
    let graph_entity = link_to(node, "speakerEntityId").or_else(|| {
        field_value(node, &["speakerEntityId"]).and_then(|v| v.as_str().map(str::to_string))
    });
    let edges = node
        .fields
        .get("edges")
        .and_then(|f| f.value.as_ref())
        .and_then(Value::as_array);
    nodes
        .iter()
        .enumerate()
        .filter_map(|(index, entry)| {
            let entry = entry.as_object()?;
            let text = |name: &str| {
                entry
                    .get(name)
                    .and_then(Value::as_str)
                    .filter(|s| !s.is_empty())
            };
            let mut values = Map::new();
            values.insert("graph".into(), json!(graph));
            values.insert("node_index".into(), json!(index));
            values.insert(
                "kind".into(),
                entry.get("kind").cloned().unwrap_or(Value::Null),
            );
            values.insert(
                "text".into(),
                entry.get("text").cloned().unwrap_or(Value::Null),
            );
            values.insert(
                "speaker_name".into(),
                text("speaker").map_or_else(|| graph_speaker.clone(), |s| json!(s)),
            );
            let speaker = text("speakerEntityId").map(str::to_string).or_else(|| {
                if text("speaker").is_some() {
                    None
                } else {
                    graph_entity.clone()
                }
            });
            values.insert(
                "speaker".into(),
                speaker
                    .filter(|s| !s.is_empty())
                    .map_or(Value::Null, Value::String),
            );
            values.insert(
                "conditions".into(),
                entry.get("condition").cloned().unwrap_or(Value::Null),
            );
            values.insert(
                "consequences".into(),
                entry.get("actions").cloned().unwrap_or(Value::Null),
            );
            values.insert(
                "options".into(),
                entry.get("options").cloned().unwrap_or(Value::Null),
            );
            values.insert(
                "edges".into(),
                json!(
                    edges
                        .into_iter()
                        .flatten()
                        .filter(|e| e.get("from").and_then(Value::as_u64) == Some(index as u64))
                        .collect::<Vec<_>>()
                ),
            );
            values.insert("removed".into(), json!(false));
            Some((format!("{graph}/{index}"), values))
        })
        .collect()
}

impl Indexer {
    /// An indexer for the binding `app` (the agent's own name). The producer state is kept in
    /// `<state_dir>/logger-producer.json`.
    pub fn new(
        client: Client,
        app: &str,
        state_dir: &std::path::Path,
        every_ms: u64,
    ) -> Arc<Indexer> {
        let inventory = state_dir.join("index-publication.json");
        let restored = match std::fs::read(&inventory) {
            Ok(bytes) => serde_json::from_slice::<SavedQueue>(&bytes).map_err(|e| e.to_string()),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(SavedQueue::default()),
            Err(e) => Err(e.to_string()),
        };
        let queue = match restored {
            Ok(saved) => Queue {
                rows: saved
                    .rows
                    .into_iter()
                    .map(|(kind, key, value)| ((kind, key), value))
                    .collect(),
                sources: saved.sources,
                revision: saved.revision,
                storage_error: None,
            },
            Err(error) => Queue {
                storage_error: Some(error),
                ..Queue::default()
            },
        };
        Arc::new(Indexer {
            client,
            app: app.to_string(),
            store: state_dir.join("logger-producer.json"),
            inventory,
            every: Duration::from_millis(every_ms.max(100)),
            queue: Mutex::new(queue),
            logger: tokio::sync::Mutex::new(None),
            wake: Notify::new(),
            closed: std::sync::atomic::AtomicBool::new(false),
        })
    }

    /// Declare the binding (retrying with backoff until it succeeds) and run the ticker.
    pub fn start(self: &Arc<Self>) -> tokio::task::JoinHandle<()> {
        let me = self.clone();
        crate::blocking::spawn(async move { me.run().await })
    }

    async fn run(self: Arc<Self>) {
        let mut delay = Duration::from_millis(500);
        loop {
            if self.closed.load(std::sync::atomic::Ordering::SeqCst) {
                return;
            }
            let opts = LoggerOptions::new(&self.app, states())
                .store(FileStore::new(self.store.clone()))
                .batch_size(10_000)
                .flush_interval(Duration::from_secs(3600));
            match self.client.logger(opts).await {
                Ok(l) => {
                    tracing::info!(app = %self.app, "resource graph binding declared");
                    *self.logger.lock().await = Some(l);
                    break;
                }
                Err(e) => {
                    tracing::warn!(app = %self.app, error = %e, "cannot declare the resource graph binding yet");
                    tokio::select! {
                        _ = tokio::time::sleep(delay) => {}
                        _ = self.wake.notified() => {}
                    }
                    delay = (delay * 2).min(Duration::from_secs(60));
                }
            }
        }
        loop {
            tokio::select! {
                _ = tokio::time::sleep(self.every) => {}
                _ = self.wake.notified() => {}
            }
            if self.closed.load(std::sync::atomic::Ordering::SeqCst) {
                return;
            }
            self.deliver().await;
        }
    }

    /// Hand the coalesced rows to the logger and run one delivery round.
    async fn deliver(&self) {
        let guard = self.logger.lock().await;
        let Some(logger) = guard.as_ref() else {
            return;
        };
        let rows = match self.queue.lock() {
            Ok(q) => q.rows.clone(),
            Err(_) => return,
        };
        if rows.is_empty() {
            return;
        }
        let mut accepted = BTreeSet::new();
        for ((kind, key), p) in &rows {
            let meta = ChangeMeta {
                op: Some(p.op.clone()),
                at: Some(now_ms()),
                by: Some(self.app.clone()),
                correlation: p.correlation.clone(),
            };
            match logger.change(kind, key, Value::Object(p.values.clone()), meta) {
                Ok(()) => {
                    accepted.insert((kind.clone(), key.clone()));
                }
                Err(e) => {
                    tracing::warn!(kind = %kind, key = %key, error = %e, "index row not accepted")
                }
            }
        }
        if let Err(e) = logger.flush().await {
            tracing::warn!(rows = rows.len(), error = %e, "index delivery failed; retained for the next round");
            return;
        }
        if let Ok(mut q) = self.queue.lock() {
            for key in accepted {
                if q.rows.get(&key) == rows.get(&key) {
                    q.rows.remove(&key);
                }
            }
            if let Err(e) = self.save(&q) {
                tracing::warn!(error = %e, "index delivery checkpoint failed; restart may replay rows");
            }
        }
    }

    fn save(&self, queue: &Queue) -> Result<(), IndexError> {
        use std::io::Write;
        #[derive(Serialize)]
        struct Snapshot<'a> {
            rows: Vec<(&'a String, &'a String, &'a Pending)>,
            sources: &'a OwnerInventory,
            revision: Option<u64>,
        }
        let snapshot = Snapshot {
            rows: queue
                .rows
                .iter()
                .map(|((kind, key), pending)| (kind, key, pending))
                .collect(),
            sources: &queue.sources,
            revision: queue.revision,
        };
        let temp = self.inventory.with_extension("tmp");
        let write = || -> Result<(), Box<dyn std::error::Error>> {
            let mut file = std::fs::File::create(&temp)?;
            serde_json::to_writer(&mut file, &snapshot)?;
            file.flush()?;
            file.sync_all()?;
            std::fs::rename(&temp, &self.inventory)?;
            if let Some(parent) = self.inventory.parent() {
                std::fs::File::open(parent)?.sync_all()?;
            }
            Ok(())
        };
        write().map_err(|e| IndexError::Storage(e.to_string()))
    }

    fn enqueue(
        &self,
        kind: &str,
        key: String,
        values: Map<String, Value>,
        op: &'static str,
        correlation: Option<String>,
    ) -> Result<(), IndexError> {
        let mut q = self.queue.lock().map_err(|_| IndexError::Poisoned)?;
        if q.rows.len() >= MAX_PENDING && !q.rows.contains_key(&(kind.to_string(), key.clone())) {
            return Err(IndexError::Backpressure(q.rows.len()));
        }
        q.rows.insert(
            (kind.to_string(), key),
            Pending {
                values,
                op: op.to_string(),
                correlation,
            },
        );
        Ok(())
    }

    /// Queue an index delta.
    pub fn ingest(&self, delta: &IndexDelta) -> Result<IndexDeltaAck, IndexError> {
        self.ingest_owned(delta, "")
    }

    /// Namespace every Resource Graph key with the authenticated app/project identity.
    pub fn ingest_owned(
        &self,
        delta: &IndexDelta,
        owner: &str,
    ) -> Result<IndexDeltaAck, IndexError> {
        let owner_hash = crate::util::sha256_hex(owner.as_bytes());
        let scoped = |key: &str| {
            if owner.is_empty() {
                key.to_string()
            } else {
                format!("{owner_hash}:{key}")
            }
        };
        // The authenticated owner, not the user-supplied display name, defines snapshot replacement.
        let namespace = if owner.is_empty() {
            delta.project.clone()
        } else {
            owner.to_string()
        };
        let specs = states();
        let mut changes = BTreeMap::new();
        let mut current = BTreeMap::new();
        let mut skipped = 0;
        let corr = Some(format!("{}@{}", delta.project, delta.revision));
        let mut project = Map::new();
        project.insert("name".into(), json!(delta.project));
        project.insert("index_revision".into(), json!(delta.revision));
        if let Some(info) = &delta.project_info {
            for (from, to) in [("unity", "unity"), ("kernelTag", "kernel_tag")] {
                if let Some(s) = info.get(from).and_then(Value::as_str) {
                    project.insert(to.into(), json!(s));
                }
            }
        }
        changes.insert(
            ("gc_project".to_string(), scoped(&delta.project)),
            Pending {
                values: project,
                op: "index".into(),
                correlation: corr.clone(),
            },
        );
        for node in &delta.nodes {
            let mut rows = Vec::new();
            if let Some((kind, key, values)) = node_row(node) {
                rows.push((kind, key, values));
            }
            for (key, values) in dialogue_rows(node) {
                rows.push(("gc_dialogue_node", key, values));
            }
            if rows.is_empty() {
                skipped += 1;
            }
            let mut keys = BTreeSet::new();
            for (kind, key, mut values) in rows {
                for link in ["region", "definition", "speaker", "graph"] {
                    if let Some(Value::String(key)) = values.get_mut(link) {
                        *key = scoped(key);
                    }
                }
                if kind == "gc_definition" || kind == "gc_dialogue_node" {
                    values.insert("owner".into(), json!(owner_hash));
                }
                // SDK traces are patches. Clear optional fields/links that disappeared in this snapshot.
                let spec = &specs[kind];
                for field in spec.props.keys().chain(spec.links.keys()) {
                    values.entry(field.clone()).or_insert(Value::Null);
                }
                let row = (kind.to_string(), scoped(&key));
                keys.insert(row.clone());
                changes.insert(
                    row,
                    Pending {
                        values,
                        op: "index".into(),
                        correlation: corr.clone(),
                    },
                );
            }
            current.insert(source_key(&node.reference), keys);
        }
        let mut q = self.queue.lock().map_err(|_| IndexError::Poisoned)?;
        if let Some(error) = &q.storage_error {
            return Err(IndexError::Storage(error.clone()));
        }
        let previous = q.sources.entry(namespace.clone()).or_default();
        let mut removed = BTreeSet::new();
        for (source, old_rows) in previous.iter() {
            if let Some(new_rows) = current.get(source) {
                removed.extend(old_rows.difference(new_rows).cloned());
            } else if delta.base_revision.is_none() {
                removed.extend(old_rows.iter().cloned());
            }
        }
        for reference in &delta.removals {
            let source = source_key(reference);
            if let Some(old_rows) = previous.get(&source) {
                removed.extend(old_rows.iter().cloned());
            } else {
                let kind = match reference.kind {
                    RefKind::Entity => "gc_entity",
                    RefKind::Definition => "gc_definition",
                    RefKind::Region => "gc_region",
                    _ => {
                        skipped += 1;
                        continue;
                    }
                };
                if let Some(key) = key_of(kind, reference, "") {
                    removed.insert((kind.to_string(), scoped(&key)));
                }
            }
        }
        // Upserts win if a source is both removed and reintroduced in a coalesced delta.
        for row in removed {
            changes.entry(row).or_insert_with(|| Pending {
                values: Map::from_iter([("removed".into(), json!(true))]),
                op: "remove".into(),
                correlation: corr.clone(),
            });
        }
        let additional = changes
            .keys()
            .filter(|key| !q.rows.contains_key(*key))
            .count();
        if q.rows.len() + additional > MAX_PENDING {
            return Err(IndexError::Backpressure(q.rows.len() + additional));
        }
        let sources = q.sources.entry(namespace).or_default();
        if delta.base_revision.is_none() {
            sources.clear();
        }
        for reference in &delta.removals {
            sources.remove(&source_key(reference));
        }
        sources.extend(current);
        let queued = changes.len();
        q.rows.extend(changes);
        q.revision = Some(q.revision.map_or(delta.revision, |r| r.max(delta.revision)));
        self.save(&q)?;
        Ok(IndexDeltaAck {
            revision: delta.revision,
            queued,
            skipped,
        })
    }

    /// Queue one row of the desk's own states (`gc_changeset`, `gc_task`, `gc_asset`).
    pub fn record(&self, kind: &str, key: &str, values: Map<String, Value>) {
        if let Err(e) = self.enqueue(kind, key.to_string(), values, "record", None) {
            tracing::warn!(kind, key, error = %e, "resource graph record dropped");
        }
    }

    /// The highest index revision received.
    pub fn revision(&self) -> Option<u64> {
        self.queue.lock().ok().and_then(|q| q.revision)
    }

    /// Rows waiting for delivery.
    pub fn pending(&self) -> usize {
        self.queue.lock().map(|q| q.rows.len()).unwrap_or(0)
    }

    /// Deliver what is queued and close the logger (on shutdown).
    pub async fn close(&self) {
        self.deliver().await;
        self.closed.store(true, std::sync::atomic::Ordering::SeqCst);
        self.wake.notify_waiters();
        if let Some(l) = self.logger.lock().await.as_ref() {
            if let Err(e) = l.close().await {
                tracing::warn!(error = %e, "index logger did not deliver everything on close");
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn node(v: Value) -> IndexNode {
        serde_json::from_value(v).unwrap()
    }

    #[test]
    fn states_are_consistent() {
        let s = states();
        assert_eq!(s.len(), 10);
        for (kind, spec) in &s {
            assert!(spec.global, "{kind}");
            for (name, target) in &spec.links {
                assert!(!spec.props.contains_key(name), "{kind}.{name}");
                assert!(s.contains_key(target), "{kind}.{name} -> {target}");
            }
        }
    }

    /// Node `type` is the `[Authorable]` type id (03 §3; the attribute is 03 §4), e.g. `npc.definition`.
    #[test]
    fn authorable_type_ids_map_to_kinds() {
        let kind = |r: Value, ty: &str| {
            node_row(&node(json!({"ref": r, "type": ty, "name": "n"}))).map(|(k, _, _)| k)
        };
        let ent = json!({"kind": "Entity", "authoringId": "e1", "definition": "npc.ferryman@3"});
        let def = |d: &str| json!({"kind": "Definition", "definition": d});
        assert_eq!(kind(ent, "npc.definition"), Some("gc_entity"));
        assert_eq!(
            kind(def("npc.ferryman@3"), "npc.definition"),
            Some("gc_definition")
        );
        assert_eq!(
            kind(def("item.lantern@2"), "item.definition"),
            Some("gc_definition")
        );
        assert_eq!(
            kind(def("dlg.ferry@1"), "dialogue.graph"),
            Some("gc_definition")
        );
        assert_eq!(
            kind(def("dlg.ferry.n7@1"), "dialogue.node"),
            Some("gc_dialogue_node")
        );
        assert_eq!(kind(def("q.ferry@1"), "quest.definition"), Some("gc_quest"));
        assert_eq!(kind(def("r.toll@1"), "logic.rule"), Some("gc_rule"));
        assert_eq!(
            kind(json!({"kind": "SceneObject", "path": "/a"}), "scene.object"),
            None
        );
    }

    #[test]
    fn nodes_map_to_rows() {
        let n = node(json!({
            "ref": {"kind": "Entity", "authoringId": "e1", "definition": "npc.ferryman@3"},
            "type": "npc.definition", "name": "Ferryman",
            "fields": {"speed": {"value": 1.8, "unit": "m/s", "type": "float"}},
            "refs": [{"field": "region", "to": {"kind": "Region", "authoringId": "marsh"}}]
        }));
        let (kind, key, v) = node_row(&n).unwrap();
        assert_eq!((kind, key.as_str()), ("gc_entity", "e1"));
        assert_eq!(v["region"], "marsh");
        assert_eq!(v["definition"], "npc.ferryman@3");
        assert_eq!(v["fields"]["speed"]["value"], 1.8);
        let d = node(
            json!({"ref": {"kind": "Definition", "authoringId": "d1", "definition": "quest.lantern@1"},
                            "type": "quest.definition", "name": "Lantern",
                            "fields": {"stages": {"value": [1, 2], "type": "int[]"}}}),
        );
        let (kind, key, v) = node_row(&d).unwrap();
        assert_eq!((kind, key.as_str()), ("gc_quest", "quest.lantern"));
        assert_eq!(v["stages"], json!([1, 2]));
        let s = node(json!({"ref": {"kind": "SceneObject", "authoringId": "s"}, "type": "x"}));
        assert!(node_row(&s).is_none());
        let explicit = node(json!({"ref": {"kind": "Definition", "authoringId": "r1"},
                                   "type": "x", "rgKind": "gc_rule", "rgKey": "rule.one"}));
        assert_eq!(node_row(&explicit).unwrap().0, "gc_rule");
        assert_eq!(node_row(&explicit).unwrap().1, "rule.one");
    }

    #[tokio::test]
    async fn deltas_coalesce_per_key() {
        let dir = tempfile::tempdir().unwrap();
        let client = Client::new("http://127.0.0.1:9", "k-0123456789abcdef").unwrap();
        let ix = Indexer::new(client, "gamecore-studio", dir.path(), 1000);
        let delta = |rev: u64, name: &str| IndexDelta {
            project: "hollowmere".into(),
            revision: rev,
            base_revision: None,
            project_info: None,
            nodes: vec![node(
                json!({"ref": {"kind": "Entity", "authoringId": "e1"}, "type": "t", "name": name}),
            )],
            edges: vec![],
            removals: vec![
                serde_json::from_value(json!({"kind": "Entity", "authoringId": "gone"})).unwrap(),
            ],
        };
        let a = ix.ingest(&delta(1, "a")).unwrap();
        assert_eq!((a.queued, a.skipped), (3, 0));
        ix.ingest(&delta(2, "b")).unwrap();
        assert_eq!(ix.pending(), 3);
        assert_eq!(ix.revision(), Some(2));
        let q = ix.queue.lock().unwrap();
        let p = q
            .rows
            .get(&("gc_entity".to_string(), "e1".to_string()))
            .unwrap();
        assert_eq!(p.values["name"], "b");
        let r = q
            .rows
            .get(&("gc_entity".to_string(), "gone".to_string()))
            .unwrap();
        assert_eq!(
            (r.op.as_str(), &r.values["removed"]),
            ("remove", &json!(true))
        );
    }
}
