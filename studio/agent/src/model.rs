//! Serde mirrors of the authoring contracts (docs/studio/03-authoring-contracts.md §1–§6,
//! §9) and the companion's own API shapes (04 §2). JSON is camelCase. Contract types accept
//! unknown fields (forward compatibility: the C# shapes in `GameCore.Studio.Model` own them);
//! the change set is additionally validated against its JSON Schema ([`crate::schema`]) and
//! stored verbatim, so nothing a worker wrote is lost by this mirror.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

// ---------------------------------------------------------------------------------------------
// §1 Authoring identity.

/// What an [`AuthoringRef`] points at.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum RefKind {
    /// An authored entity.
    Entity,
    /// A definition asset.
    Definition,
    /// A region scene.
    Region,
    /// A plain scene object.
    SceneObject,
    /// An asset.
    Asset,
    /// A runtime UI element.
    UiElement,
    /// A GameCore scope.
    Scope,
    /// A point in the world.
    Location,
}

/// What the user chose to edit.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum EditScope {
    /// This instance.
    Instance,
    /// The prefab.
    Prefab,
    /// The definition.
    Definition,
    /// The scope.
    Scope,
}

/// A world location (kind `Location`).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Location {
    /// Owning region id.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub region: Option<String>,
    /// Position in metres.
    pub position: [f64; 3],
    /// Surface normal.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub normal: Option<[f64; 3]>,
}

/// `AuthoringRef`: a pointer to an authored thing, stable across sessions.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AuthoringRef {
    /// Kind.
    pub kind: RefKind,
    /// GUID on the object/asset (absent for Location and Asset).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub authoring_id: Option<String>,
    /// Unity GlobalObjectId.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub global: Option<String>,
    /// Asset GUID.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub asset_guid: Option<String>,
    /// Asset path plus hierarchy path.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub path: Option<String>,
    /// `name@revision` of its definition.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub definition: Option<String>,
    /// Edit scope.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub scope: Option<EditScope>,
    /// Content stamp at selection time.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub stamp: Option<String>,
    /// Location (kind `Location`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub location: Option<Location>,
}

// ---------------------------------------------------------------------------------------------
// §2 Selection.

/// A sub-part selection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SelectedPart {
    /// The logical owner.
    pub owner: AuthoringRef,
    /// The part (`Mesh:Lantern_Glass`).
    pub part: String,
}

/// `SelectionSnapshot`, captured when a prompt is sent.
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SelectionSnapshot {
    /// Snapshot id.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    /// `Edit` or `Play`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mode: Option<String>,
    /// Logical targets.
    #[serde(default)]
    pub targets: Vec<AuthoringRef>,
    /// Picked parts.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub parts: Vec<SelectedPart>,
    /// Box selection rectangle.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub region_rect: Option<Value>,
    /// Camera and frame (the image by digest, never inline).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub frame: Option<Value>,
    /// World session in Play.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub world_session: Option<String>,
    /// Semantic index revision of the snapshot.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub index_revision: Option<u64>,
}

// ---------------------------------------------------------------------------------------------
// §3 Semantic index.

/// A field of an index node.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexField {
    /// Current value.
    pub value: Value,
    /// Unit.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub unit: Option<String>,
    /// Allowed range.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub range: Option<Value>,
    /// Field type.
    #[serde(default, rename = "type", skip_serializing_if = "Option::is_none")]
    pub ty: Option<String>,
}

/// A reference held by a node's field.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexRefLink {
    /// The field.
    pub field: String,
    /// The target.
    pub to: AuthoringRef,
}

/// One node of the semantic index.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexNode {
    /// What it is.
    #[serde(rename = "ref")]
    pub reference: AuthoringRef,
    /// Its authored type (`npc.NpcDefinition`).
    #[serde(default, rename = "type")]
    pub ty: String,
    /// Display name.
    #[serde(default)]
    pub name: String,
    /// Fields.
    #[serde(default)]
    pub fields: BTreeMap<String, IndexField>,
    /// References.
    #[serde(default)]
    pub refs: Vec<IndexRefLink>,
    /// Capabilities (`dialogue.speaker`).
    #[serde(default)]
    pub capabilities: Vec<String>,
    /// Where it comes from.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub provenance: Option<Value>,
    /// Explicit Resource Graph kind (`gc_quest`), overriding the companion's mapping.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub rg_kind: Option<String>,
    /// Explicit Resource Graph key, overriding the companion's mapping.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub rg_key: Option<String>,
}

/// An edge of the semantic index.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexEdge {
    /// From.
    pub from: AuthoringRef,
    /// To.
    pub to: AuthoringRef,
    /// `references | contains | spawns | bindsUi | triggers`.
    pub kind: String,
}

/// `POST /v1/index/delta`: the nodes changed since `baseRevision`, the removed refs, and the
/// new revision. Edges are accepted for completeness; the Resource Graph mapping uses the
/// nodes' own `refs`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexDelta {
    /// Project name.
    pub project: String,
    /// The new revision.
    pub revision: u64,
    /// The revision this delta applies to.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub base_revision: Option<u64>,
    /// Project facts for `gc_project` (`unity`, `kernelTag`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub project_info: Option<Map<String, Value>>,
    /// Added or changed nodes.
    #[serde(default)]
    pub nodes: Vec<IndexNode>,
    /// Edges.
    #[serde(default)]
    pub edges: Vec<IndexEdge>,
    /// Removed things.
    #[serde(default)]
    pub removals: Vec<AuthoringRef>,
}

/// The answer to an index delta.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IndexDeltaAck {
    /// The revision recorded.
    pub revision: u64,
    /// Rows queued for the next batch.
    pub queued: usize,
    /// Nodes with no Resource Graph kind (scene objects, assets, UI, scopes, locations).
    pub skipped: usize,
}

// ---------------------------------------------------------------------------------------------
// §6 Change set.

/// The change set's intent.
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Intent {
    /// The prompt text.
    pub text: String,
    /// The voice transcript it came from.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub voice_transcript_id: Option<String>,
    /// `agent | manual | voice | replay`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub origin: Option<String>,
}

/// One operation of a change set.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Operation {
    /// Id within the change set.
    pub op_id: String,
    /// The tool (`inventory.grantStarting`).
    pub tool: String,
    /// Target.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub target: Option<AuthoringRef>,
    /// Arguments.
    #[serde(default)]
    pub args: Map<String, Value>,
    /// Ops this depends on.
    #[serde(default)]
    pub depends_on: Vec<String>,
    /// `stamp` or `none`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub preconditions: Option<String>,
    /// `Live | Rebuild | Compile | Build`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub apply_requirement: Option<String>,
}

/// One artifact of a change set (referenced by digest).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ArtifactEntry {
    /// SHA-256 (`<hex>` or `sha256:<hex>`).
    pub sha256: String,
    /// File name.
    pub name: String,
    /// Media type.
    pub media_type: String,
    /// Size.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub bytes: Option<u64>,
    /// Who produced it (`etosTask`, `op`, `provider`, `model`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub producer: Option<Value>,
    /// Role (`voiceLine`, `texture`, `package`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub role: Option<String>,
    /// Import settings.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub import: Option<Value>,
}

/// Links of a change set.
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChangeSetLinks {
    /// etos tasks.
    #[serde(default)]
    pub etos_tasks: Vec<String>,
    /// Parent change set.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub parent: Option<String>,
    /// GameCore operations.
    #[serde(default)]
    pub game_core_ops: Vec<String>,
}

/// A change set (`gamecore.studio.changeset/1`), as far as the companion reads it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChangeSet {
    /// Change-set id.
    pub id: String,
    /// Schema name.
    pub schema: String,
    /// Intent.
    pub intent: Intent,
    /// Selection.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub selection: Option<SelectionSnapshot>,
    /// Operations.
    #[serde(default)]
    pub operations: Vec<Operation>,
    /// Artifacts.
    #[serde(default)]
    pub artifacts: Vec<ArtifactEntry>,
    /// Links.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub links: Option<ChangeSetLinks>,
    /// State.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub state: Option<String>,
}

/// The change-set schema name.
pub const CHANGESET_SCHEMA: &str = "gamecore.studio.changeset/1";

// ---------------------------------------------------------------------------------------------
// §9 Diagnostics.

/// `{code, message, hint, where}`: every refusal and validation failure.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Diagnostic {
    /// Code.
    pub code: String,
    /// Message.
    pub message: String,
    /// Hint.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hint: Option<String>,
    /// Where: an op id, a JSON pointer, a file, or an `AuthoringRef`.
    #[serde(default, rename = "where", skip_serializing_if = "Option::is_none")]
    pub location: Option<Value>,
}

impl Diagnostic {
    /// A diagnostic with a code and a message.
    pub fn new(code: &str, message: impl Into<String>) -> Diagnostic {
        Diagnostic {
            code: code.to_string(),
            message: message.into(),
            hint: None,
            location: None,
        }
    }

    /// The same diagnostic located.
    pub fn at(mut self, location: impl Into<Value>) -> Diagnostic {
        self.location = Some(location.into());
        self
    }
}

// ---------------------------------------------------------------------------------------------
// Companion API (04 §2).

/// An attachment of an edit request: bytes inline (base64), uploaded to etos as an input.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Attachment {
    /// File name as the worker sees it under `/inputs` (`frame.png`).
    pub name: String,
    /// Media type.
    pub media_type: String,
    /// Role (`frame`, `reference`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub role: Option<String>,
    /// The bytes, base64 (standard alphabet).
    pub data: String,
    /// Optional digest, checked when given.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sha256: Option<String>,
}

/// `POST /v1/requests`: `EditRequest`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EditRequest {
    /// Minted by Unity; the request id and the etos task's request id.
    pub change_set_id: String,
    /// What the user asked.
    pub intent: Intent,
    /// What was selected.
    #[serde(default)]
    pub selection: SelectionSnapshot,
    /// The bounded index slice (03 §3), packed as `index-slice.json`.
    #[serde(default)]
    pub context_slice: Value,
    /// The tool catalog revision the request was built against.
    pub tool_catalog_revision: Value,
    /// The tool catalog itself; optional when the companion already holds that revision.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub tool_catalog: Option<Value>,
    /// The worker (`gc-designer`, `gc-mechanic`); the default worker when absent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub worker: Option<String>,
    /// Files for the worker (`frame.png`).
    #[serde(default)]
    pub attachments: Vec<Attachment>,
}

/// Companion request states (etos task status is reported verbatim beside it).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum RequestState {
    /// Persisted; the task is not open yet.
    Requested,
    /// The task is open (queued, starting or running).
    Running,
    /// etos left the task waiting (budget, a question); never retried by the companion.
    Waiting,
    /// A validated candidate is available.
    Candidate,
    /// The worker's output failed validation (and no re-ask is pending).
    CandidateInvalid,
    /// The worker asked one clarification question.
    NeedsClarification,
    /// The task failed.
    Failed,
    /// The task was cancelled.
    Cancelled,
    /// etos does not know the outcome (`unknown`, `outcome_unknown`); never a success.
    Unresolved,
}

impl RequestState {
    /// The wire name.
    pub fn as_str(self) -> &'static str {
        match self {
            RequestState::Requested => "requested",
            RequestState::Running => "running",
            RequestState::Waiting => "waiting",
            RequestState::Candidate => "candidate",
            RequestState::CandidateInvalid => "candidate_invalid",
            RequestState::NeedsClarification => "needs_clarification",
            RequestState::Failed => "failed",
            RequestState::Cancelled => "cancelled",
            RequestState::Unresolved => "unresolved",
        }
    }

    /// Parse a wire name.
    pub fn parse(s: &str) -> Option<RequestState> {
        Some(match s {
            "requested" => RequestState::Requested,
            "running" => RequestState::Running,
            "waiting" => RequestState::Waiting,
            "candidate" => RequestState::Candidate,
            "candidate_invalid" => RequestState::CandidateInvalid,
            "needs_clarification" => RequestState::NeedsClarification,
            "failed" => RequestState::Failed,
            "cancelled" => RequestState::Cancelled,
            "unresolved" => RequestState::Unresolved,
            _ => return None,
        })
    }

    /// Whether the companion stops following the request.
    pub fn is_terminal(self) -> bool {
        matches!(
            self,
            RequestState::Candidate
                | RequestState::CandidateInvalid
                | RequestState::NeedsClarification
                | RequestState::Failed
                | RequestState::Cancelled
        )
    }
}

/// A request as `/v1/requests` answers it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RequestView {
    /// The request id (equal to the change-set id).
    pub request_id: String,
    /// The change-set id.
    pub change_set_id: String,
    /// The worker.
    pub worker: String,
    /// Companion state.
    pub state: RequestState,
    /// The current attempt's etos task id.
    #[serde(default)]
    pub task_id: Option<String>,
    /// The etos task status, verbatim (`queued|starting|running|waiting|done|failed|cancelled|unknown`).
    #[serde(default)]
    pub task_status: Option<String>,
    /// The current attempt's topic.
    #[serde(default)]
    pub topic: Option<String>,
    /// 0 for the first task, 1 for the re-ask.
    pub attempt: u32,
    /// Every etos task opened for the request, oldest first.
    #[serde(default)]
    pub tasks: Vec<String>,
    /// What ended it: `{code, message, hint?, text?, diagnostics?}`.
    #[serde(default)]
    pub outcome: Option<Value>,
    /// Whether `/v1/candidates/{id}` has a candidate.
    pub has_candidate: bool,
    /// Ledger position of the last change (the `after` cursor of `/v1/requests`).
    pub seq: i64,
    /// Created (ms).
    pub created_at: i64,
    /// Updated (ms).
    pub updated_at: i64,
}

/// `GET /v1/requests?after=` answer.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RequestList {
    /// Requests changed after `after`, oldest change first.
    pub requests: Vec<RequestView>,
    /// The `after` of the next call.
    pub next: i64,
}

/// An artifact as the companion stores and serves it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StoredArtifact {
    /// Lowercase hex SHA-256.
    pub sha256: String,
    /// Name.
    pub name: String,
    /// Media type.
    pub media_type: String,
    /// Size in bytes.
    pub bytes: u64,
    /// Producer facts.
    #[serde(default)]
    pub producer: Value,
    /// Role.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub role: Option<String>,
    /// Relative URL of the bytes (`/v1/artifacts/<sha256>`).
    pub url: String,
}

/// `GET /v1/candidates/{id}`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CandidateView {
    /// The change-set id.
    pub change_set_id: String,
    /// The etos task that produced it.
    pub task_id: String,
    /// Attempt.
    pub attempt: u32,
    /// The validated change set, verbatim.
    pub change_set: Value,
    /// Its artifacts, verified and stored.
    pub artifacts: Vec<StoredArtifact>,
    /// Non-fatal findings (unlisted output files).
    #[serde(default)]
    pub diagnostics: Vec<Diagnostic>,
    /// When it was accepted (ms).
    pub received_at: i64,
}

/// One event of `WS /v1/events`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EventView {
    /// Monotonic cursor (reconnect with `after=<cursor>`).
    pub cursor: i64,
    /// When (ms).
    pub at: i64,
    /// `request | task_progress | candidate | candidate_invalid | clarification | voice_session | voice_transcript | stage | asset`.
    #[serde(rename = "type")]
    pub kind: String,
    /// The request it concerns.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
    /// Payload.
    pub data: Value,
}

/// Status of one capability in `/v1/hello`: `live | not_configured | blocked | unknown`.
pub type ProviderStatus = String;

/// `GET /v1/hello`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Hello {
    /// `gamecore-studio`.
    pub service: String,
    /// Companion version.
    pub version: String,
    /// Protocol version.
    pub protocol: u32,
    /// The calling app.
    pub app: String,
    /// The node's name (from the welcome).
    #[serde(default)]
    pub node: Option<String>,
    /// The node's SDK API version.
    #[serde(default)]
    pub sdk: Option<String>,
    /// Whether the agent channel is connected.
    pub connected: bool,
    /// Feature names.
    pub capabilities: Vec<String>,
    /// `image`, `tts`, `voice`, `3d`, `describe`.
    pub providers: BTreeMap<String, ProviderStatus>,
    /// When the provider status was read (ms).
    #[serde(default)]
    pub providers_checked_at: Option<i64>,
    /// Workers requests may name.
    pub workers: Vec<String>,
    /// Tool catalog revisions the companion holds.
    pub tool_catalog_revisions: Vec<String>,
    /// The highest index revision received.
    #[serde(default)]
    pub index_revision: Option<u64>,
}

/// `POST /v1/ops/generate`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GenerateRequest {
    /// `image | tts | 3d | describe`.
    pub op: String,
    /// The operation's own input (etops: `prompt`, `size`, `text`, `voice`, ...). For
    /// `describe`, `artifact` (a stored digest) or `input` (an etos reference) names the file.
    #[serde(default)]
    pub spec: Map<String, Value>,
    /// Cost ceiling (`max_cost_usd` of etops).
    #[serde(default, rename = "max_cost_usd", alias = "maxCostUsd")]
    pub max_cost_usd: Option<f64>,
    /// The change set the asset is for (also makes the op idempotent per spec).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub change_set_id: Option<String>,
}

/// `POST /v1/ops/generate` answer.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GenerateResponse {
    /// The op as asked.
    pub op: String,
    /// The etos operation run (`generate.image`).
    pub etos_op: String,
    /// Provider that served it.
    #[serde(default)]
    pub provider: Option<String>,
    /// The etops job state (`succeeded`, `failed`, ...), verbatim.
    #[serde(default)]
    pub state: Value,
    /// Produced files, verified and stored.
    #[serde(default)]
    pub artifacts: Vec<StoredArtifact>,
    /// Text (`describe`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub text: Option<String>,
    /// The etops idempotency key used.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub key: Option<String>,
}

/// `POST /v1/stage`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StageRequest {
    /// The change set proposing the package.
    pub change_set_id: String,
    /// The package artifact (`.tgz`/`.tar.gz`/`.tar`) by digest.
    pub package_ref: String,
}

/// A stage job.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StageJobView {
    /// Job id.
    pub job_id: String,
    /// The change set.
    pub change_set_id: String,
    /// The package digest.
    pub package_ref: String,
    /// `queued | running | done | failed`.
    pub state: String,
    /// Slot used.
    #[serde(default)]
    pub slot: Option<String>,
    /// The verdict `{ok, compile, tests, forbidden, durationMs}` (from `stage.sh`), or the
    /// failure `{code, message, hint}`.
    #[serde(default)]
    pub verdict: Option<Value>,
    /// Created (ms).
    pub created_at: i64,
    /// Updated (ms).
    pub updated_at: i64,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn contract_examples_round_trip() {
        let r: AuthoringRef = serde_json::from_value(json!({
            "kind": "Location",
            "location": {"region": "marsh", "position": [12.5, 0.0, -3.25], "normal": [0, 1, 0]}
        }))
        .unwrap();
        assert_eq!(r.kind, RefKind::Location);
        let cs: ChangeSet = serde_json::from_value(json!({
            "id": "cs_1", "schema": CHANGESET_SCHEMA,
            "intent": {"text": "Give the ferryman a lantern", "origin": "agent"},
            "operations": [{"opId": "op1", "tool": "inventory.grantStarting",
                            "target": {"kind": "Entity", "authoringId": "7f1c"},
                            "args": {"item": "item.lantern@2", "count": 1}, "dependsOn": []}],
            "artifacts": [{"sha256": "ab", "name": "a.wav", "mediaType": "audio/wav", "bytes": 4}],
            "futureField": true
        }))
        .unwrap();
        assert_eq!(cs.operations[0].op_id, "op1");
        assert_eq!(cs.artifacts[0].media_type, "audio/wav");
        let v = serde_json::to_value(&cs).unwrap();
        assert_eq!(v["operations"][0]["opId"], "op1");
        assert_eq!(
            serde_json::to_value(RequestState::CandidateInvalid).unwrap(),
            json!("candidate_invalid")
        );
        assert_eq!(
            RequestState::parse("unresolved"),
            Some(RequestState::Unresolved)
        );
    }

    #[test]
    fn edit_requests_parse() {
        let r: EditRequest = serde_json::from_value(json!({
            "changeSetId": "cs_1", "intent": {"text": "x"},
            "selection": {"targets": [], "indexRevision": 3},
            "contextSlice": {"revision": 3, "nodes": []},
            "toolCatalogRevision": 7
        }))
        .unwrap();
        assert_eq!(r.selection.index_revision, Some(3));
        assert!(r.attachments.is_empty());
        let g: GenerateRequest = serde_json::from_value(
            json!({"op": "image", "spec": {"prompt": "p"}, "max_cost_usd": 0.1}),
        )
        .unwrap();
        assert_eq!(g.max_cost_usd, Some(0.1));
    }
}
