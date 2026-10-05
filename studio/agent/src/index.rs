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

use std::collections::BTreeMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use etos_sdk::wire::{PropSpec, PropType, StateSpec};
use etos_sdk::{ChangeMeta, Client, FileStore, Logger, LoggerOptions};
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
                .prop("asset_path", PropType::String),
        ),
    );
    m.insert(
        "gc_dialogue_node".into(),
        removed(
            g("node")
                .prop("graph", PropType::String)
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

#[derive(Debug, Clone)]
struct Pending {
    values: Map<String, Value>,
    op: &'static str,
    correlation: Option<String>,
}

#[derive(Default)]
struct Queue {
    rows: BTreeMap<(String, String), Pending>,
    revision: Option<u64>,
}

/// The index logger.
pub struct Indexer {
    client: Client,
    app: String,
    store: PathBuf,
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

impl Indexer {
    /// An indexer for the binding `app` (the agent's own name). The producer state is kept in
    /// `<state_dir>/logger-producer.json`.
    pub fn new(
        client: Client,
        app: &str,
        state_dir: &std::path::Path,
        every_ms: u64,
    ) -> Arc<Indexer> {
        Arc::new(Indexer {
            client,
            app: app.to_string(),
            store: state_dir.join("logger-producer.json"),
            every: Duration::from_millis(every_ms.max(100)),
            queue: Mutex::new(Queue::default()),
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
        let rows = match self.queue.lock() {
            Ok(mut q) => std::mem::take(&mut q.rows),
            Err(_) => return,
        };
        let guard = self.logger.lock().await;
        let Some(logger) = guard.as_ref() else {
            // Not declared yet: put them back.
            if let Ok(mut q) = self.queue.lock() {
                for (k, v) in rows {
                    q.rows.entry(k).or_insert(v);
                }
            }
            return;
        };
        let n = rows.len();
        for ((kind, key), p) in rows {
            let meta = ChangeMeta {
                op: Some(p.op.to_string()),
                at: Some(now_ms()),
                by: Some(self.app.clone()),
                correlation: p.correlation,
            };
            if let Err(e) = logger.change(&kind, &key, Value::Object(p.values), meta) {
                tracing::warn!(kind = %kind, key = %key, error = %e, "index row dropped");
            }
        }
        if let Err(e) = logger.flush().await {
            tracing::warn!(rows = n, error = %e, "index delivery failed; the SDK retries what it can");
        }
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
                op,
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
        let scoped = |key: &str| {
            if owner.is_empty() {
                key.to_string()
            } else {
                format!("{}:{key}", crate::util::sha256_hex(owner.as_bytes()))
            }
        };
        let mut queued = 0;
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
        self.enqueue(
            "gc_project",
            scoped(&delta.project),
            project,
            "index",
            corr.clone(),
        )?;
        queued += 1;
        for node in &delta.nodes {
            match node_row(node) {
                Some((kind, key, mut values)) => {
                    for link in ["region", "definition", "speaker", "graph"] {
                        if let Some(Value::String(key)) = values.get_mut(link) {
                            *key = scoped(key);
                        }
                    }
                    self.enqueue(kind, scoped(&key), values, "index", corr.clone())?;
                    queued += 1;
                }
                None => skipped += 1,
            }
        }
        for r in &delta.removals {
            let kind = match r.kind {
                RefKind::Entity => "gc_entity",
                RefKind::Definition => "gc_definition",
                RefKind::Region => "gc_region",
                _ => {
                    skipped += 1;
                    continue;
                }
            };
            let Some(key) = key_of(kind, r, "") else {
                skipped += 1;
                continue;
            };
            let mut v = Map::new();
            v.insert("removed".into(), Value::Bool(true));
            self.enqueue(kind, scoped(&key), v, "remove", corr.clone())?;
            queued += 1;
        }
        if let Ok(mut q) = self.queue.lock() {
            q.revision = Some(q.revision.map_or(delta.revision, |r| r.max(delta.revision)));
        }
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
        assert_eq!((r.op, &r.values["removed"]), ("remove", &json!(true)));
    }
}
