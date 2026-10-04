//! Wire types of the app routes (logger, sources, entrances, query) and the agent routes (tools,
//! the endpoint, services, changes, tasks, topics, files), written by hand from the
//! node's JSON Schema (`crates/etapi/schema/etapi.json`; the `schema` test keeps them equal).
//! Field names travel in snake_case; times are integer milliseconds since the Unix epoch;
//! every non-2xx body is a [`Refusal`].

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

pub use crate::error::Refusal;

// ---------------------------------------------------------------------------------------------
// Bindings (the logger configuration).

/// Property types a state can declare.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum PropType {
    /// Short text.
    String,
    /// Free text (indexed for text search).
    Text,
    /// One of a set of names (`values`).
    Enum,
    /// A 64-bit integer.
    Int,
    /// A number.
    Number,
    /// An amount: a number or decimal text.
    Money,
    /// True or false.
    Bool,
    /// A time in milliseconds since the epoch.
    Time,
    /// Any JSON.
    Json,
}

/// A typed property of a state. `values` restricts an `enum`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PropSpec {
    /// The type.
    #[serde(rename = "type")]
    pub ty: PropType,
    /// The allowed values of an `enum`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub values: Option<Vec<String>>,
    /// One line for the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
}

impl From<PropType> for PropSpec {
    fn from(ty: PropType) -> PropSpec {
        PropSpec {
            ty,
            values: None,
            description: None,
        }
    }
}

/// How the Resource Graph holds a state.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum StateMode {
    /// Values, history and links are copied from the trace stream.
    #[default]
    Mirrored,
    /// Only identities, keys and links; values stay in the app's database.
    Referenced,
}

/// One declared state.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StateSpec {
    /// Name of the key property.
    pub key: String,
    /// Typed properties by name.
    #[serde(default)]
    pub props: BTreeMap<String, PropSpec>,
    /// Link name to target state kind.
    #[serde(default)]
    pub links: BTreeMap<String, String>,
    /// Mirrored or referenced.
    #[serde(default)]
    pub mode: StateMode,
    /// Sent only to the user's local node.
    #[serde(default)]
    pub private: bool,
    /// Shared across users (the `global` partition; traces carry `user: null`).
    #[serde(default)]
    pub global: bool,
    /// One line for the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
}

impl StateSpec {
    /// A mirrored state keyed by `key`, with no properties or links yet.
    pub fn new(key: &str) -> StateSpec {
        StateSpec {
            key: key.to_string(),
            props: BTreeMap::new(),
            links: BTreeMap::new(),
            mode: StateMode::Mirrored,
            private: false,
            global: false,
            description: None,
        }
    }

    /// Add a property.
    pub fn prop(mut self, name: &str, spec: impl Into<PropSpec>) -> StateSpec {
        self.props.insert(name.to_string(), spec.into());
        self
    }

    /// Add a link to another declared state.
    pub fn link(mut self, name: &str, target: &str) -> StateSpec {
        self.links.insert(name.to_string(), target.to_string());
        self
    }

    /// Shared across users.
    pub fn global(mut self) -> StateSpec {
        self.global = true;
        self
    }

    /// Referenced instead of mirrored.
    pub fn referenced(mut self) -> StateSpec {
        self.mode = StateMode::Referenced;
        self
    }
}

/// `PUT /api/v1/bindings/{app}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BindingDeclaration {
    /// `sha256:<hex>` of the canonical JSON of `states`.
    pub digest: String,
    /// The states, by kind.
    pub states: BTreeMap<String, StateSpec>,
}

/// Answer to a binding declaration.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BindingAck {
    /// The app.
    pub app: String,
    /// The binding version now current.
    pub version: u64,
    /// False when the declaration matched the current version.
    pub changed: bool,
}

/// One logged change.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Trace {
    /// Producer id, stable across restarts of one logger.
    pub producer: String,
    /// Strictly increasing per producer; the node deduplicates on (producer, seq).
    pub seq: u64,
    /// Partition: the user id, or null for `global` states.
    #[serde(default)]
    pub user: Option<String>,
    /// The state kind.
    pub kind: String,
    /// The entity key.
    pub key: String,
    /// Changed properties and links only.
    #[serde(default)]
    pub values: Map<String, Value>,
    /// The operation name.
    #[serde(default)]
    pub op: Option<String>,
    /// Event time in the app, in ms since the epoch.
    #[serde(default)]
    pub at: Option<i64>,
    /// Who caused the change.
    #[serde(default)]
    pub by: Option<String>,
    /// Groups the changes of one request.
    #[serde(default)]
    pub correlation: Option<String>,
}

/// `POST /api/v1/bindings/{app}/traces`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TraceBatch {
    /// The traces, in producer order.
    pub traces: Vec<Trace>,
}

/// One trace the node refused.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TraceRefusal {
    /// Its index in the batch.
    pub index: u64,
    /// Its producer.
    pub producer: String,
    /// Its sequence number.
    pub seq: u64,
    /// Why it was refused.
    pub refusal: Refusal,
}

/// Answer to a trace batch.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TraceAck {
    /// Traces appended now.
    pub accepted: u64,
    /// Traces received earlier (same producer and seq).
    pub duplicates: u64,
    /// Traces refused one by one.
    #[serde(default)]
    pub refused: Vec<TraceRefusal>,
}

/// A snapshot row.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BackfillRow {
    /// The entity key.
    pub key: String,
    /// Its properties and links.
    #[serde(default)]
    pub values: Map<String, Value>,
}

/// `POST /api/v1/bindings/{app}/backfill`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BackfillPage {
    /// The logger's producer id.
    pub producer: String,
    /// The state kind.
    pub kind: String,
    /// The user, or null for a `global` state.
    #[serde(default)]
    pub user: Option<String>,
    /// Resume token that produced this page (null for the first page).
    #[serde(default)]
    pub token: Option<String>,
    /// Token of the next page; null on the last page.
    #[serde(default)]
    pub next: Option<String>,
    /// The rows.
    #[serde(default)]
    pub rows: Vec<BackfillRow>,
    /// True on the last page.
    #[serde(default)]
    pub done: bool,
}

/// Answer to a backfill page.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BackfillAck {
    /// Rows of the page the node holds.
    pub accepted: u64,
}

// ---------------------------------------------------------------------------------------------
// App sources.

/// Field types of a source schema.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum FieldType {
    /// Text.
    String,
    /// A 64-bit integer.
    Int,
    /// A floating-point number.
    Number,
    /// True or false.
    Bool,
    /// A time: milliseconds since the Unix epoch on the wire.
    Time,
    /// Any JSON value.
    Json,
}

/// One field of a collection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FieldSchema {
    /// The type.
    #[serde(rename = "type")]
    pub ty: FieldType,
    /// Whether it may be null.
    #[serde(default)]
    pub nullable: bool,
    /// One line for the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
    /// The allowed values, when it is an enumeration.
    #[serde(default, rename = "enum", skip_serializing_if = "Option::is_none")]
    pub values: Option<Vec<String>>,
}

/// A foreign key.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Relation {
    /// The field holding the reference.
    pub field: String,
    /// The referenced collection.
    pub collection: String,
    /// The referenced field.
    pub references: String,
}

/// One collection (a table) of a source.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CollectionSchema {
    /// One line for the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
    /// The key fields.
    pub key: Vec<String>,
    /// The fields, by name.
    pub fields: BTreeMap<String, FieldSchema>,
    /// Foreign keys.
    #[serde(default)]
    pub relations: Vec<Relation>,
}

/// The schema of a source.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceSchema {
    /// Collections by name.
    pub collections: BTreeMap<String, CollectionSchema>,
}

/// Which RG state a collection's rows are, and by which field.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct StateMap {
    /// The state kind.
    pub state: String,
    /// The field holding the state's key.
    pub key: String,
}

/// Filter operators.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum FilterOp {
    /// Equal.
    Eq,
    /// Not equal.
    Ne,
    /// Less than.
    Lt,
    /// Less than or equal.
    Lte,
    /// Greater than.
    Gt,
    /// Greater than or equal.
    Gte,
    /// One of a list.
    In,
    /// Starts with.
    Prefix,
    /// Text match (all words, case-insensitive).
    Match,
    /// Is null (`true`) or is not null (`false`).
    IsNull,
}

/// What a source can do with one field.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct FieldCapability {
    /// The operators it can filter with.
    #[serde(default)]
    pub ops: Vec<FilterOp>,
    /// Whether it can sort by it.
    #[serde(default)]
    pub sortable: bool,
}

/// How a collection pages.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Pagination {
    /// A response may carry a cursor for the next page.
    Cursor,
    /// One page only; never a cursor.
    None,
}

/// A request parameter a collection accepts.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ParamSpec {
    /// The type.
    #[serde(rename = "type")]
    pub ty: FieldType,
    /// Whether every request must give it.
    #[serde(default)]
    pub required: bool,
    /// One line for the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
}

/// What a source can do with one collection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CollectionCapabilities {
    /// Per-field capabilities.
    #[serde(default)]
    pub fields: BTreeMap<String, FieldCapability>,
    /// The largest page it answers.
    pub max_page: u32,
    /// Whether it pages.
    pub pagination: Pagination,
    /// Parameters it accepts.
    #[serde(default)]
    pub params: BTreeMap<String, ParamSpec>,
}

/// What a source can do.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Capabilities {
    /// Per-collection capabilities.
    pub collections: BTreeMap<String, CollectionCapabilities>,
}

/// Which rows of a collection a user may see.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ScopeRule {
    /// `field` must equal the caller's user.
    User {
        /// The field holding the user id.
        field: String,
    },
    /// Shared across users.
    Global,
}

/// `PUT /api/v1/sources/{id}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceRegistration {
    /// The schema.
    pub schema: SourceSchema,
    /// Collections that are RG states.
    #[serde(default)]
    pub mapping: BTreeMap<String, StateMap>,
    /// What the source can do.
    pub capabilities: Capabilities,
    /// The scope rule of each collection.
    pub scope: BTreeMap<String, ScopeRule>,
    /// Registered only with the user's local node.
    #[serde(default)]
    pub private: bool,
}

/// Answer to a source registration.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SourceAck {
    /// The source id.
    pub id: String,
    /// The registration version.
    pub version: u64,
}

/// A filter tree.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum Filter {
    /// All must hold.
    And {
        /// The children.
        and: Vec<Filter>,
    },
    /// At least one must hold.
    Or {
        /// The children.
        or: Vec<Filter>,
    },
    /// Must not hold.
    Not {
        /// The child.
        not: Box<Filter>,
    },
    /// A comparison.
    Leaf(FilterLeaf),
}

/// One comparison.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FilterLeaf {
    /// The field.
    pub field: String,
    /// The operator.
    pub op: FilterOp,
    /// The operand.
    pub value: Value,
}

/// Sort direction.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum SortDir {
    /// Ascending.
    Asc,
    /// Descending.
    Desc,
}

/// One sort key.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SortKey {
    /// The field.
    pub field: String,
    /// The direction.
    pub dir: SortDir,
}

/// Whose rows a request may return.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct RequestScope {
    /// The user, or null for the global partition only.
    #[serde(default)]
    pub user: Option<String>,
}

/// A structured read request the node sends to an app source.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StructuredRequest {
    /// Request id; the response names it.
    pub request: String,
    /// The collection.
    pub collection: String,
    /// Fields to return; empty means all.
    pub projection: Vec<String>,
    /// The filter.
    pub filter: Option<Filter>,
    /// The order.
    pub sort: Vec<SortKey>,
    /// Page size.
    pub limit: u32,
    /// Continue after this cursor.
    pub cursor: Option<String>,
    /// Declared parameters.
    pub params: Map<String, Value>,
    /// The caller's scope.
    pub scope: RequestScope,
}

/// The answer to a structured request.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StructuredResponse {
    /// The request id.
    pub request: String,
    /// The rows.
    pub rows: Vec<Map<String, Value>>,
    /// The next page's cursor.
    pub cursor: Option<String>,
    /// True unless the source knows the answer is partial.
    pub complete: bool,
    /// When the rows were read (ms since the epoch).
    pub read_at: i64,
    /// Why the request could not be answered.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub refusal: Option<Refusal>,
}

/// `GET /api/v1/sources/{id}/requests`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SourceRequestBatch {
    /// Pending requests.
    pub requests: Vec<StructuredRequest>,
}

/// Answer to `POST /api/v1/sources/{id}/responses`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct SourceResponseAck {
    /// Whether the request was still waiting.
    pub pending: bool,
}

// ---------------------------------------------------------------------------------------------
// Entrances.

/// An attachment sent with a user request.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AttachmentUpload {
    /// The file name.
    pub name: String,
    /// The media type.
    pub media_type: String,
    /// The content, base64.
    pub data: String,
}

/// `POST /api/v1/entrances/{name}/requests`. Idempotent on `request`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EntranceRequest {
    /// Client-chosen request id.
    pub request: String,
    /// The app's user who asks.
    pub user: String,
    /// The request text.
    pub text: String,
    /// Files sent with the request.
    #[serde(default)]
    pub attachments: Vec<AttachmentUpload>,
    /// Continue an existing conversation; null starts a new one.
    #[serde(default)]
    pub conversation: Option<String>,
}

/// Answer to an entrance request.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EntranceAck {
    /// The conversation id.
    pub conversation: String,
}

/// Whether a reference is a fixed snapshot or follows a live directory.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ReferenceKind {
    /// An immutable snapshot.
    Pinned,
    /// A live file or directory on its owner node.
    Live,
}

/// A reference to a file or directory.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Reference {
    /// The reference id.
    pub id: String,
    /// The display name.
    pub name: String,
    /// Pinned or live.
    pub kind: ReferenceKind,
    /// Size in bytes, when known.
    #[serde(default)]
    pub size: Option<u64>,
    /// The node that owns it.
    pub owner: String,
}

/// One event of a conversation; `pos` increases strictly.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ConversationEvent {
    /// Work in progress.
    Progress {
        /// Position in the conversation.
        pos: u64,
        /// When it was recorded.
        at: i64,
        /// What is happening.
        text: String,
    },
    /// The agent's answer.
    Result {
        /// Position in the conversation.
        pos: u64,
        /// When it was recorded.
        at: i64,
        /// The answer.
        text: String,
        /// Files the agent produced.
        references: Vec<Reference>,
    },
    /// The request could not be answered.
    Error {
        /// Position in the conversation.
        pos: u64,
        /// When it was recorded.
        at: i64,
        /// Why.
        refusal: Refusal,
    },
}

impl ConversationEvent {
    /// The event's position.
    pub fn pos(&self) -> u64 {
        match self {
            ConversationEvent::Progress { pos, .. }
            | ConversationEvent::Result { pos, .. }
            | ConversationEvent::Error { pos, .. } => *pos,
        }
    }
}

/// `GET /api/v1/entrances/{name}/conversations/{id}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ConversationPage {
    /// Events after `after`, in order.
    pub events: Vec<ConversationEvent>,
    /// True when every request of the conversation has been answered.
    pub done: bool,
}

/// The value of one bind parameter: `true`, `3`, `2.5` or `"text"` (also a time in RFC 3339).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum ParamValue {
    /// A boolean.
    Bool(bool),
    /// A 64-bit integer.
    Int(i64),
    /// A floating-point number.
    Float(f64),
    /// Text.
    Text(String),
}

impl From<bool> for ParamValue {
    fn from(v: bool) -> ParamValue {
        ParamValue::Bool(v)
    }
}

impl From<i64> for ParamValue {
    fn from(v: i64) -> ParamValue {
        ParamValue::Int(v)
    }
}

impl From<i32> for ParamValue {
    fn from(v: i32) -> ParamValue {
        ParamValue::Int(i64::from(v))
    }
}

impl From<u32> for ParamValue {
    fn from(v: u32) -> ParamValue {
        ParamValue::Int(i64::from(v))
    }
}

impl From<f64> for ParamValue {
    fn from(v: f64) -> ParamValue {
        ParamValue::Float(v)
    }
}

impl From<&str> for ParamValue {
    fn from(v: &str) -> ParamValue {
        ParamValue::Text(v.to_string())
    }
}

impl From<String> for ParamValue {
    fn from(v: String) -> ParamValue {
        ParamValue::Text(v)
    }
}

/// Values for the named parameters of one query: every placeholder needs a value, and every
/// value must be used.
pub type Params = BTreeMap<String, ParamValue>;

// ---------------------------------------------------------------------------------------------
// Queries.

/// `POST /api/v1/query`.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct QueryRequest {
    /// SQL.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sql: Option<String>,
    /// GraphQL.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub graphql: Option<String>,
    /// GraphQL variables.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub variables: Option<Map<String, Value>>,
    /// Typed bind parameters for `:name` placeholders.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub params: Params,
    /// Narrow the scope to one user's partition.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub user: Option<String>,
    /// Bypass the cache.
    #[serde(default)]
    pub fresh: bool,
    /// Continue a paged result.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cursor: Option<String>,
    /// For an agent's key only: run the query as this app, one whose manifest `uses` the
    /// agent; it sees exactly that app's tables under their own names.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub app: Option<String>,
    /// Plan the SQL without running it: the answer carries `columns` and a `plan`.
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub describe: bool,
}

/// A result column.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ResultColumn {
    /// Its name.
    pub name: String,
    /// Its type, as the engine names it.
    #[serde(rename = "type")]
    pub ty: String,
}

/// A source that contributed to an answer.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ResultSource {
    /// The source.
    pub source: String,
    /// When it was read.
    pub read_at: i64,
    /// Whether the answer came from the cache.
    pub cached: bool,
    /// Whether its part is complete.
    pub complete: bool,
}

/// The answer to a query.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct QueryResponse {
    /// Columns of a SQL answer.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub columns: Option<Vec<ResultColumn>>,
    /// Rows of a SQL answer.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub rows: Option<Vec<Map<String, Value>>>,
    /// Data of a GraphQL answer.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub data: Option<Value>,
    /// Repairs the engine made to the query.
    pub repairs: Vec<String>,
    /// Sources read.
    pub sources: Vec<ResultSource>,
    /// Whether the answer is complete.
    pub complete: bool,
    /// The next page's cursor.
    #[serde(default)]
    pub cursor: Option<String>,
    /// What a `describe` request planned.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub plan: Option<QueryPlan>,
}

/// A planned read (`describe`): what it would bind and read.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct QueryPlan {
    /// The names of its `:name` placeholders, sorted.
    pub params: Vec<String>,
    /// The tables it reads, each once.
    pub tables: Vec<PlannedTable>,
}

/// A table a planned read reads.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PlannedTable {
    /// The table as queries name it (`order`, `market.quotes`).
    pub name: String,
    /// The app whose source or binding provides it.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub app: Option<String>,
    /// Its key columns.
    pub key: Vec<String>,
    /// Its columns with their types.
    pub columns: Vec<ResultColumn>,
    /// The RG state it serves, whose changes the change feed reports under this name.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub state: Option<String>,
}

// ---------------------------------------------------------------------------------------------
// Agents (design-agents §5): tools, the endpoint, services, the change feed, tasks, topics
// and files.

/// Whether the node may deliver a tool call again after a node restart.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Retry {
    /// Repeating it is harmless; the node delivers it again with the same id and the agent
    /// deduplicates (the SDK's result cache helps).
    Safe,
    /// An interrupted call is reported as "outcome unknown" and not repeated (the default).
    #[default]
    Never,
}

/// `PUT /tools/{agent}.{tool}`: a tool the agent gives workers.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ToolRegistration {
    /// What the tool does, for the model (at most 4096 bytes).
    pub description: String,
    /// The JSON Schema of its arguments: an object schema.
    #[serde(default = "object_schema")]
    pub input_schema: Value,
    /// Whether a call interrupted by a node restart is delivered again.
    #[serde(default)]
    pub retry: Retry,
    /// The call's time limit in seconds, 1 to 3600 (the node's default: 120).
    #[serde(default = "default_tool_timeout")]
    pub timeout_s: u64,
}

fn object_schema() -> Value {
    serde_json::json!({"type": "object", "properties": {}})
}

fn default_tool_timeout() -> u64 {
    120
}

/// The answer to `PUT` and `DELETE /tools/{agent}.{tool}`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ToolAck {
    /// The tool, `<agent>.<tool>`.
    pub tool: String,
    /// Whether it is registered now.
    pub registered: bool,
}

/// `PUT /agent/endpoint`: the loopback listener apps reach through the node's proxy.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EndpointRequest {
    /// `http://127.0.0.1:<port>[/<base path>]` (or `localhost`, `[::1]`).
    pub url: String,
}

/// The answer to `PUT /agent/endpoint`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EndpointAck {
    /// The agent.
    pub agent: String,
    /// The listener, as registered.
    pub url: String,
    /// The token the node adds to every proxied request as `X-Etos-Proxy-Token`.
    pub proxy_token: String,
}

/// One service method an agent declares.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ServiceMethod {
    /// The JSON Schema every input must satisfy (the node checks a subset: `type`,
    /// `properties`, `required`, `additionalProperties`, `items`, `enum`, `minimum`,
    /// `maximum`, `minLength`, `maxLength`, `minItems`, `maxItems`).
    #[serde(default = "empty_object")]
    pub input_schema: Value,
    /// How long the node waits for the answer before refusing the call with `deadline`.
    pub deadline_ms: u64,
    /// The apps that may call it; absent: every app whose manifest `uses` the agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub apps: Option<Vec<String>>,
}

fn empty_object() -> Value {
    Value::Object(Map::new())
}

/// `PUT /services/{agent}`: every method of the agent (replacing the previous declaration).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ServiceDeclaration {
    /// The methods by name (lowercase letters, digits, `-`, `_`, `.`).
    pub methods: BTreeMap<String, ServiceMethod>,
}

/// The answer to a service declaration.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ServiceAck {
    /// The agent.
    pub agent: String,
    /// The declaration's version (the same declaration keeps its version).
    pub version: u64,
    /// Whether this declaration changed anything.
    pub changed: bool,
    /// The methods now declared.
    pub methods: Vec<String>,
}

/// One entry of the RG change feed.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChangeEntry {
    /// Its position in the feed (increasing, never reused).
    pub pos: u64,
    /// The app whose state changed.
    pub app: String,
    /// The state (the table's name in queries).
    pub table: String,
    /// The user partition.
    pub partition: String,
    /// The entity key.
    pub key: String,
    /// When (ms since the epoch).
    pub at: i64,
}

/// `GET /changes?after=&tables=&limit=&wait_ms=`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChangePage {
    /// Changes after `after` of the tables the caller reads, oldest first.
    pub changes: Vec<ChangeEntry>,
    /// The `after` of the next request (every position up to it has been looked at).
    pub next: u64,
}

/// `POST /tasks`: a task for one of the agent's workers.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct TaskRequest {
    /// The worker (one the agent's manifest defines).
    pub worker: String,
    /// What to do.
    pub text: String,
    /// The task's origin channel: one of the agent's own topics (`#agent/<agent>/<name>`);
    /// absent, a new topic for this request.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub topic: Option<String>,
    /// Pinned references the agent may read, given to the task in `/inputs`.
    #[serde(default)]
    pub inputs: Vec<String>,
    /// The agent's id of this request: a repeat with the same id returns the same task.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
}

/// A task an agent opened, as `POST /tasks` and `GET /tasks/{id}` answer.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TaskInfo {
    /// The task id.
    pub task: String,
    /// Its worker.
    pub worker: String,
    /// Its origin channel.
    pub topic: String,
    /// `queued`, `starting`, `running`, `waiting`, `done`, `failed` or `cancelled`.
    pub status: String,
    /// Its result, when done.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub result: Option<Value>,
    /// Why it failed or was cancelled.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// When it was opened (ms since the epoch).
    pub created_at: i64,
    /// When it ended (ms since the epoch).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub ended_at: Option<i64>,
}

/// A worker's task status on a topic record.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum RecordStatus {
    /// Working on it.
    Working,
    /// Waiting for something.
    Waiting,
    /// Finished.
    Done,
    /// Gave up or failed.
    Failed,
}

/// `POST /topics/{topic}/records`.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct TopicPost {
    /// The text.
    #[serde(default)]
    pub text: String,
    /// A structured payload.
    #[serde(default, skip_serializing_if = "Value::is_null")]
    pub data: Value,
    /// Reference ids the record carries.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub refs: Vec<String>,
    /// A task status.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub status: Option<RecordStatus>,
    /// The agent's id of this post: a repeat with the same id is not posted again.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
}

/// The answer to a post.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct TopicAck {
    /// The topic.
    pub topic: String,
    /// The record's position.
    pub pos: u64,
    /// Whether this repeated an earlier post (same id), which was not posted again.
    pub repeated: bool,
}

/// One record of a topic.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TopicRecord {
    /// Its position, from 1.
    pub pos: u64,
    /// Who posted it: an actor id, `agent:<name>`, `app:<name>`, `human:<name>` or `node`.
    pub sender: String,
    /// When (ms since the epoch).
    pub at: i64,
    /// The text.
    pub text: String,
    /// A structured payload.
    #[serde(default, skip_serializing_if = "Value::is_null")]
    pub data: Value,
    /// Reference ids it carries.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub refs: Vec<String>,
    /// A task status.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub status: Option<RecordStatus>,
}

/// `GET /topics/{topic}/records?after=&limit=&wait_ms=`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TopicPage {
    /// Records after `after`, in order.
    pub records: Vec<TopicRecord>,
    /// The topic's last position when it was read (0 before its first record).
    pub head: u64,
}

/// A file an agent uploaded (`POST /files`): a pinned reference.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileInfo {
    /// The reference.
    #[serde(flatten)]
    pub reference: Reference,
    /// Its media type.
    pub media_type: String,
    /// Its content digest (`sha256:<hex>`).
    pub digest: String,
}

/// `POST /tickets`: a ticket for one WebSocket to a proxied path (for an app's browser code,
/// which cannot set `Authorization` on a WebSocket).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct TicketRequest {
    /// The path the WebSocket opens, `/api/v1/agents/{agent}/http/<path>`, of an agent the
    /// app uses (no query).
    pub path: String,
}

/// A WebSocket ticket: open `<path>?etos_ticket=<ticket>` without `Authorization`, once,
/// within `expires_in` seconds.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct TicketAck {
    /// The ticket.
    pub ticket: String,
    /// Seconds until it expires.
    pub expires_in: u64,
}
