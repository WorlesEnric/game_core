//! Evaluation of a finished task's outputs into a candidate.
//!
//! The worker writes `/outputs/changeset.json` (a `gamecore.studio.changeset/1` document whose
//! `artifacts[]` lists every asset with its SHA-256) and the assets themselves; etos shares
//! them as pinned references on the task's final record. The companion fetches every
//! reference and identifies the documents by content (references carry no names on the agent
//! routes): the change set by its `schema`, a clarification by `status:
//! "needs-clarification"`. The candidate is valid when:
//!
//! 1. exactly one change set is present and it satisfies the JSON Schema
//!    (`docs/studio/schemas/change-set.schema.json`, P0.3);
//! 2. its `id` is the requested change-set id; it has operations; op ids are unique;
//!    `dependsOn` resolve and form no cycle;
//! 3. every manifest entry is listed once and its digest is the digest of a delivered file
//!    (and its `bytes` the file's size): a claimed asset that was not written, or whose bytes
//!    differ, is `artifact_digest_mismatch`;
//! 4. every `{"artifact": "sha256:<hex>"}` in an operation's arguments is well formed and in
//!    the manifest, and every manifest entry is used by an operation.
//!
//! 5. it is a candidate (03 §9 candidate mode): `state` absent or `Candidate`, no `outcomes`,
//!    no `timestamps.applied`, no `links.gameCoreOps`; and it holds no `null` (null policy);
//! 6. bounded tool-catalog rules (04 §4), against the catalog of the request's revision:
//!    every tool is in the catalog (`UnknownTool`); required arguments are present and no
//!    unknown argument is given, a required target is present and its kind is allowed
//!    (`InvalidArgs`); a target's edit scope is allowed (`ScopeNotAllowed`). A catalog that
//!    is gone or whose digest is not the request's revision is `StaleContext`.
//!
//! Structural findings are `CandidateInvalid` diagnostics (03 §9) naming the rule at the
//! start of the message, the same rules the Unity `ChangeSetValidator` applies; the catalog
//! rules use the validator's own codes. The validator's index-dependent checks (targets in
//! the index, stamps, prerequisites, value ranges, requirements) stay with the engine.
//! Delivered files not in the manifest are reported (`output_unlisted`) but not stored.

use std::collections::{BTreeSet, HashMap};

use serde_json::Value;

use crate::model::{ArtifactEntry, CHANGESET_SCHEMA, ChangeSet, Diagnostic, Operation};
use crate::schema::ChangeSetSchema;
use crate::util::{catalog_revision, first_null, normalize_sha256};

/// Code of a tool the catalog does not contain (03 §9 registry).
pub const UNKNOWN_TOOL: &str = "UnknownTool";
/// Code of a missing, unknown or misplaced argument or target.
pub const INVALID_ARGS: &str = "InvalidArgs";
/// Code of an edit scope the tool does not allow.
pub const SCOPE_NOT_ALLOWED: &str = "ScopeNotAllowed";
/// Code of a candidate built against a catalog revision the companion cannot confirm.
pub const STALE_CONTEXT: &str = "StaleContext";

/// The tool catalog a candidate is checked against.
#[derive(Debug, Clone, Copy)]
pub struct CatalogContext<'a> {
    /// The request's `toolCatalogRevision` (lowercase hex).
    pub revision: &'a str,
    /// The catalog held for that revision, if any.
    pub catalog: Option<&'a Value>,
}

/// One fetched output reference.
#[derive(Debug, Clone, PartialEq)]
pub struct Fetched {
    /// The etos reference id.
    pub reference: String,
    /// Its bytes.
    pub bytes: Vec<u8>,
    /// Lowercase hex SHA-256 of the bytes.
    pub sha256: String,
}

/// A valid candidate.
#[derive(Debug, Clone, PartialEq)]
pub struct ValidCandidate {
    /// The change set with unambiguous missing scopes normalized.
    pub raw: Value,
    /// Its typed reading.
    pub change_set: ChangeSet,
    /// Manifest entries (digest normalised) with the index of the delivering file.
    pub artifacts: Vec<(ArtifactEntry, usize)>,
    /// Non-fatal findings.
    pub warnings: Vec<Diagnostic>,
}

/// What the outputs amount to.
#[derive(Debug, Clone, PartialEq)]
pub enum Evaluation {
    /// A valid change set.
    Valid(Box<ValidCandidate>),
    /// One clarification question instead of a change set.
    Clarification {
        /// The question.
        question: String,
        /// The document as written.
        raw: Value,
    },
    /// Rejected, with the reasons.
    Invalid(Vec<Diagnostic>),
}

fn json_object(bytes: &[u8]) -> Option<Value> {
    let v: Value = serde_json::from_slice(bytes).ok()?;
    v.is_object().then_some(v)
}

/// Evaluate the outputs of a task opened for `expected_id`, checked against `catalog`.
pub fn evaluate(
    files: &[Fetched],
    expected_id: &str,
    schema: &ChangeSetSchema,
    catalog: CatalogContext<'_>,
) -> Evaluation {
    let mut changesets = Vec::new();
    let mut clarifications = Vec::new();
    for (i, f) in files.iter().enumerate() {
        let Some(v) = json_object(&f.bytes) else {
            continue;
        };
        if v.get("schema").and_then(Value::as_str) == Some(CHANGESET_SCHEMA) {
            changesets.push((i, v));
        } else if v.get("status").and_then(Value::as_str) == Some("needs-clarification") {
            clarifications.push((i, v));
        }
    }
    if changesets.is_empty() {
        if let [(_, c)] = clarifications.as_slice() {
            let question = c
                .get("question")
                .and_then(Value::as_str)
                .map(str::trim)
                .unwrap_or_default();
            if question.is_empty() {
                return Evaluation::Invalid(vec![Diagnostic::candidate(
                    "clarification_invalid",
                    "the clarification has no `question`",
                )]);
            }
            return Evaluation::Clarification {
                question: question.to_string(),
                raw: c.clone(),
            };
        }
        let mut d = Diagnostic::candidate(
            "changeset_missing",
            format!(
                "the task's {} output file(s) hold no change set (`schema: {CHANGESET_SCHEMA}`)",
                files.len()
            ),
        );
        d.hint = Some("the worker must write /outputs/changeset.json".into());
        return Evaluation::Invalid(vec![d]);
    }
    if changesets.len() > 1 || !clarifications.is_empty() {
        return Evaluation::Invalid(vec![Diagnostic::candidate(
            "ambiguous_output",
            format!(
                "the outputs hold {} change sets and {} clarifications; exactly one document is allowed",
                changesets.len(),
                clarifications.len()
            ),
        )]);
    }
    let (cs_index, mut raw) = changesets.remove(0);
    let mut violations = Vec::new();
    if let Some(at) = first_null(&raw) {
        violations.push(
            Diagnostic::candidate(
                "null_member",
                "optional members are omitted when absent, never written as null",
            )
            .at_path(at),
        );
    }
    violations.extend(schema.check(&raw));
    if !violations.is_empty() {
        return Evaluation::Invalid(violations);
    }
    let mut change_set: ChangeSet = match serde_json::from_value(raw.clone()) {
        Ok(c) => c,
        Err(e) => {
            return Evaluation::Invalid(vec![Diagnostic::candidate(
                "changeset_unreadable",
                format!("the change set does not fit the contract: {e}"),
            )]);
        }
    };
    let mut errors = candidate_mode(&raw);
    let mut inferences = Vec::new();
    errors.extend(catalog_rules(
        &mut change_set.operations,
        catalog,
        &mut inferences,
    ));
    // Keep the persisted wire document and typed reading identical without changing unrelated fields.
    if let Some(operations) = raw["operations"].as_array_mut() {
        for (operation, normalized) in operations.iter_mut().zip(&change_set.operations) {
            if let Some(scope) = normalized.target.as_ref().and_then(|target| target.scope) {
                if let Ok(value) = serde_json::to_value(scope) {
                    operation["target"]["scope"] = value;
                }
            }
        }
    }
    if change_set.id != expected_id {
        errors.push(
            Diagnostic::candidate(
                "changeset_id_mismatch",
                format!(
                    "the change set says id {:?}, the request is {expected_id:?}",
                    change_set.id
                ),
            )
            .at_path("/id")
            .with_hint("copy the change-set id from request.md"),
        );
    }
    if change_set.operations.is_empty() {
        errors.push(Diagnostic::candidate(
            "no_operations",
            "the change set has no operations",
        ));
    }
    let mut op_ids = BTreeSet::new();
    for op in &change_set.operations {
        if !op_ids.insert(op.op_id.as_str()) {
            errors.push(
                Diagnostic::candidate(
                    "duplicate_op_id",
                    format!("operation id {:?} is used more than once", op.op_id),
                )
                .at_op(&op.op_id),
            );
        }
    }
    for op in &change_set.operations {
        for dep in &op.depends_on {
            if !op_ids.contains(dep.as_str()) {
                errors.push(
                    Diagnostic::candidate(
                        "unknown_dependency",
                        format!(
                            "operation {:?} depends on unknown operation {dep:?}",
                            op.op_id
                        ),
                    )
                    .at_op(&op.op_id),
                );
            }
        }
    }
    if let Some(cycle) = dependency_cycle(&change_set) {
        errors.push(Diagnostic::candidate(
            "dependency_cycle",
            format!(
                "operations depend on each other in a cycle: {}",
                cycle.join(" -> ")
            ),
        ));
    }
    let by_digest: HashMap<&str, usize> = files
        .iter()
        .enumerate()
        .filter(|(i, _)| *i != cs_index)
        .map(|(i, f)| (f.sha256.as_str(), i))
        .collect();
    let mut artifacts = Vec::new();
    let mut listed = BTreeSet::new();
    for (n, a) in change_set.artifacts.iter().enumerate() {
        let Some(h) = normalize_sha256(&a.sha256) else {
            errors.push(
                Diagnostic::candidate(
                    "artifact_digest_invalid",
                    format!("{:?} is not a sha256 (64 lowercase hex digits)", a.sha256),
                )
                .at_path(format!("/artifacts/{n}/sha256")),
            );
            continue;
        };
        if !listed.insert(h.clone()) {
            errors.push(
                Diagnostic::candidate(
                    "artifact_duplicate",
                    format!("artifact sha256:{h} is listed more than once"),
                )
                .at_path(format!("/artifacts/{n}")),
            );
            continue;
        }
        let Some(&i) = by_digest.get(h.as_str()) else {
            errors.push(
                Diagnostic::candidate(
                    "artifact_digest_mismatch",
                    format!(
                        "no delivered output file has sha256 {h} (claimed for {:?})",
                        a.name
                    ),
                )
                .at_path(format!("/artifacts/{n}"))
                .with_hint(
                    "write the asset to /outputs and list the sha256 of the bytes actually written",
                ),
            );
            continue;
        };
        if let Some(b) = a.bytes.filter(|b| *b != files[i].bytes.len() as u64) {
            errors.push(
                Diagnostic::candidate(
                    "artifact_size_mismatch",
                    format!(
                        "{:?} claims {b} bytes, the delivered file has {}",
                        a.name,
                        files[i].bytes.len()
                    ),
                )
                .at_path(format!("/artifacts/{n}/bytes")),
            );
            continue;
        }
        let mut entry = a.clone();
        entry.sha256 = h;
        artifacts.push((entry, i));
    }
    let mut used = BTreeSet::new();
    for op in &change_set.operations {
        let mut refs = Vec::new();
        collect_artifact_refs(&Value::Object(op.args.clone()), &mut refs);
        for r in refs {
            let Some(h) = strict_artifact_ref(&r) else {
                errors.push(
                    Diagnostic::candidate(
                        "artifact_reference_invalid",
                        format!(
                            "artifact reference {r:?} of operation {:?} is not 'sha256:' plus 64 lowercase hex digits",
                            op.op_id
                        ),
                    )
                    .at_op(&op.op_id),
                );
                continue;
            };
            if listed.contains(h) {
                used.insert(h.to_string());
            } else {
                errors.push(
                    Diagnostic::candidate(
                        "artifact_unlisted_reference",
                        format!(
                            "operation {:?} uses artifact {r}, which the change set does not carry",
                            op.op_id
                        ),
                    )
                    .at_op(&op.op_id)
                    .with_hint(
                        "list every referenced artifact under `artifacts` with its digest, size and media type",
                    ),
                );
            }
        }
    }
    for h in listed.difference(&used) {
        errors.push(
            Diagnostic::candidate(
                "artifact_unused",
                format!("artifact sha256:{h} is carried but no operation uses it"),
            )
            .with_hint(
                "remove the artifact or reference it from an operation argument as {\"artifact\": \"sha256:...\"}",
            ),
        );
    }
    if !errors.is_empty() {
        return Evaluation::Invalid(errors);
    }
    let mut warnings: Vec<Diagnostic> = files
        .iter()
        .enumerate()
        .filter(|(i, f)| *i != cs_index && !listed.contains(&f.sha256))
        .map(|(_, f)| {
            Diagnostic::candidate(
                "output_unlisted",
                format!(
                    "output {} (sha256 {}) is not in the manifest and was not kept",
                    f.reference, f.sha256
                ),
            )
        })
        .collect();
    warnings.extend(inferences);
    Evaluation::Valid(Box::new(ValidCandidate {
        raw,
        change_set,
        artifacts,
        warnings,
    }))
}

/// Candidate-mode findings (03 §9): a worker's change set is a candidate, never a record of
/// an apply.
fn candidate_mode(raw: &Value) -> Vec<Diagnostic> {
    let mut d = Vec::new();
    if let Some(state) = raw
        .get("state")
        .filter(|state| state.as_str() != Some("Candidate"))
    {
        d.push(
            Diagnostic::candidate(
                "candidate_mode",
                format!("`state` is {state}; a worker's change set has no state or `Candidate`"),
            )
            .at_path("/state"),
        );
    }
    for (pointer, what) in [
        ("/outcomes", "`outcomes`"),
        ("/timestamps/applied", "`timestamps.applied`"),
        ("/links/gameCoreOps", "`links.gameCoreOps`"),
    ] {
        if raw.pointer(pointer).is_some() {
            d.push(
                Diagnostic::candidate(
                    "candidate_mode",
                    format!("{what} records an apply; a worker's change set cannot carry it"),
                )
                .at_path(pointer),
            );
        }
    }
    d
}

fn str_list<'a>(v: &'a Value, key: &str) -> Option<Vec<&'a str>> {
    v.get(key)
        .and_then(Value::as_array)
        .map(|a| a.iter().filter_map(Value::as_str).collect())
}

/// The bounded catalog rules (04 §4) for every operation.
fn catalog_rules(
    ops: &mut [Operation],
    ctx: CatalogContext<'_>,
    inferences: &mut Vec<Diagnostic>,
) -> Vec<Diagnostic> {
    let catalog = match ctx.catalog {
        Some(c) => {
            // The catalog's own `revision` (when present) and its content digest must both be
            // the revision the request was built against.
            let actual = c
                .get("revision")
                .and_then(Value::as_str)
                .filter(|r| *r != ctx.revision)
                .map(str::to_string)
                .unwrap_or_else(|| catalog_revision(c));
            if actual != ctx.revision {
                return vec![
                    Diagnostic::new(
                        STALE_CONTEXT,
                        "the tool catalog held for the request is not the revision it was built against",
                    )
                    .with_data(serde_json::json!({"expected": ctx.revision, "actual": actual})),
                ];
            }
            c
        }
        None => {
            return vec![
                Diagnostic::new(
                    STALE_CONTEXT,
                    format!(
                        "the companion no longer holds tool catalog revision {}",
                        ctx.revision
                    ),
                )
                .with_data(serde_json::json!({"expected": ctx.revision})),
            ];
        }
    };
    let tools: HashMap<&str, &Value> = catalog
        .get("tools")
        .and_then(Value::as_array)
        .map(|a| {
            a.iter()
                .filter_map(|t| t.get("id").and_then(Value::as_str).map(|id| (id, t)))
                .collect()
        })
        .unwrap_or_default();
    let mut d = Vec::new();
    for op in ops {
        let Some(tool) = tools.get(op.tool.as_str()) else {
            d.push(
                Diagnostic::new(
                    UNKNOWN_TOOL,
                    format!(
                        "operation {:?} uses tool {:?}, which the catalog does not contain",
                        op.op_id, op.tool
                    ),
                )
                .at_op(&op.op_id)
                .with_hint("use only the tools listed in tool-catalog.json"),
            );
            continue;
        };
        let args: Vec<(&str, bool)> = tool
            .get("args")
            .and_then(Value::as_array)
            .map(|a| {
                a.iter()
                    .filter_map(|x| {
                        let name = x.get("name")?.as_str()?;
                        Some((
                            name,
                            x.get("required").and_then(Value::as_bool).unwrap_or(false),
                        ))
                    })
                    .collect()
            })
            .unwrap_or_default();
        for name in op.args.keys() {
            if !args.iter().any(|(n, _)| n == name) {
                d.push(
                    Diagnostic::new(
                        INVALID_ARGS,
                        format!("tool {:?} has no argument {name:?}", op.tool),
                    )
                    .at_op(&op.op_id),
                );
            }
        }
        for (name, required) in &args {
            if *required && !op.args.contains_key(*name) {
                d.push(
                    Diagnostic::new(
                        INVALID_ARGS,
                        format!("tool {:?} requires argument {name:?}", op.tool),
                    )
                    .at_op(&op.op_id),
                );
            }
        }
        let target_required = tool
            .get("targetRequired")
            .and_then(Value::as_bool)
            .unwrap_or(false);
        if op.tool == "entity.applyOverride"
            && tool.get("targetType").and_then(Value::as_str) == Some("entity.instance")
            && op
                .target
                .as_ref()
                .is_some_and(|t| t.kind == crate::model::RefKind::Entity)
            && op.args.get("field").and_then(Value::as_str) == Some("tint")
        {
            if let Some(value) = op.args.get("value") {
                let valid = value.as_str().is_some_and(|text| {
                    text.is_empty()
                        || (text.len() == 7
                            && text.starts_with('#')
                            && text.as_bytes()[1..].iter().all(u8::is_ascii_hexdigit))
                });
                if !valid {
                    d.push(Diagnostic::new(INVALID_ARGS,
                        "GP-ENT-004: instance override tint must be #rrggbb (six hex digits), or empty to clear it.")
                        .at_op(&op.op_id).with_data(serde_json::json!({"contract":"GP-ENT-004","field":"tint","expected":"#rrggbb","actual":value})));
                }
            }
        }
        match &mut op.target {
            None if target_required => d.push(
                Diagnostic::new(INVALID_ARGS, format!("tool {:?} needs a target", op.tool))
                    .at_op(&op.op_id),
            ),
            None => {}
            Some(t) => {
                let kind = serde_json::to_value(t.kind)
                    .ok()
                    .and_then(|v| v.as_str().map(str::to_string))
                    .unwrap_or_default();
                if let Some(kinds) =
                    str_list(tool, "targetKinds").filter(|kinds| !kinds.contains(&kind.as_str()))
                {
                    d.push(
                        Diagnostic::new(
                            INVALID_ARGS,
                            format!(
                                "tool {:?} does not accept target kind {kind} (accepts {})",
                                op.tool,
                                kinds.join(", ")
                            ),
                        )
                        .at_op(&op.op_id),
                    );
                }
                // Companion has the request catalog, but no complete index. Use the declared
                // target type; Unity additionally intersects with the actual indexed type.
                let type_entry = tool
                    .get("targetType")
                    .and_then(Value::as_str)
                    .and_then(|id| {
                        catalog
                            .get("objectTypes")
                            .and_then(Value::as_array)?
                            .iter()
                            .find(|entry| entry.get("typeId").and_then(Value::as_str) == Some(id))
                    });
                let tool_scopes = str_list(tool, "scopes");
                let type_scopes = type_entry.and_then(|entry| str_list(entry, "scopes"));
                let mut allowed: Option<BTreeSet<&str>> = tool_scopes
                    .as_ref()
                    .or(type_scopes.as_ref())
                    .map(|scopes| scopes.iter().copied().collect());
                if let (Some(allowed), Some(type_scopes)) = (&mut allowed, &type_scopes) {
                    allowed.retain(|scope| type_scopes.contains(scope));
                }
                if t.scope.is_none() {
                    if let Some(scope) = allowed
                        .as_ref()
                        .filter(|choices| choices.len() == 1)
                        .and_then(|choices| choices.first())
                        .copied()
                    {
                        t.scope = serde_json::from_value(Value::String(scope.into())).ok();
                        if t.scope.is_some() {
                            inferences.push(Diagnostic::new("ScopeInferred",
                                format!("Target scope inferred as {scope} from the tool/type intersection."))
                                .at_op(&op.op_id).with_data(serde_json::json!({"inferred":true,"scope":scope})));
                        }
                    }
                }
                if let Some(allowed) = allowed {
                    let scope = t
                        .scope
                        .and_then(|scope| serde_json::to_value(scope).ok())
                        .and_then(|value| value.as_str().map(str::to_owned));
                    if !scope
                        .as_ref()
                        .is_some_and(|scope| allowed.contains(scope.as_str()))
                    {
                        d.push(Diagnostic::new(SCOPE_NOT_ALLOWED,
                            format!("tool {:?} requires a target scope in the tool/type intersection (allowed: {})",
                                op.tool, allowed.into_iter().collect::<Vec<_>>().join(", "))).at_op(&op.op_id));
                    }
                }
            }
        }
    }
    d
}

/// Whether a re-ask can help: not for a catalog the companion cannot confirm.
pub fn reaskable(diagnostics: &[Diagnostic]) -> bool {
    !diagnostics.iter().any(|d| d.code == STALE_CONTEXT)
}

/// `sha256:<64 lowercase hex>` (the reference form of 03 §6), as the bare digest.
fn strict_artifact_ref(r: &str) -> Option<&str> {
    let h = r.strip_prefix("sha256:")?;
    (h.len() == 64
        && h.bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b)))
    .then_some(h)
}

/// The operations left on a dependency cycle (known dependencies only), if any.
fn dependency_cycle(cs: &ChangeSet) -> Option<Vec<String>> {
    let ids: BTreeSet<&str> = cs.operations.iter().map(|o| o.op_id.as_str()).collect();
    let mut pending: HashMap<&str, BTreeSet<&str>> = HashMap::new();
    for op in &cs.operations {
        let deps = pending.entry(op.op_id.as_str()).or_default();
        deps.extend(
            op.depends_on
                .iter()
                .map(String::as_str)
                .filter(|d| ids.contains(d)),
        );
    }
    loop {
        let ready: Vec<&str> = pending
            .iter()
            .filter(|(_, deps)| deps.is_empty())
            .map(|(id, _)| *id)
            .collect();
        if ready.is_empty() {
            break;
        }
        for id in ready {
            pending.remove(id);
            for deps in pending.values_mut() {
                deps.remove(id);
            }
        }
    }
    if pending.is_empty() {
        return None;
    }
    let mut left: Vec<String> = pending.keys().map(|s| s.to_string()).collect();
    left.sort();
    Some(left)
}

/// Every string under an `artifact` key, at any depth.
fn collect_artifact_refs(v: &Value, out: &mut Vec<String>) {
    match v {
        Value::Object(map) => {
            for (k, child) in map {
                if let Some(s) = child.as_str().filter(|_| k == "artifact") {
                    out.push(s.to_string());
                } else {
                    collect_artifact_refs(child, out);
                }
            }
        }
        Value::Array(items) => items.iter().for_each(|i| collect_artifact_refs(i, out)),
        _ => {}
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::util::sha256_hex;
    use serde_json::json;

    const ID: &str = "cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXY";

    static CATALOG: std::sync::LazyLock<Value> = std::sync::LazyLock::new(|| {
        json!({"schema": "gamecore.studio.toolcatalog/1", "objectTypes": [], "tools": [
            {"id": "dialogue.addNode", "tier": "Compose", "runtimeApply": "Live", "targetRequired": false,
             "args": [{"name": "voice", "type": "artifact", "required": false}]},
            {"id": "x.y", "tier": "Configure", "runtimeApply": "Live", "targetRequired": false, "args": []},
            {"id": "inventory.grantStarting", "tier": "Configure", "runtimeApply": "Live", "targetRequired": true,
             "targetKinds": ["Entity"], "scopes": ["Instance", "Definition"],
             "args": [{"name": "item", "type": "ref", "required": true}, {"name": "count", "type": "int", "required": false}]}
        ]})
    });
    static REVISION: std::sync::LazyLock<String> =
        std::sync::LazyLock::new(|| catalog_revision(&CATALOG));

    fn ctx() -> CatalogContext<'static> {
        CatalogContext {
            revision: &REVISION,
            catalog: Some(&CATALOG),
        }
    }
    const OTHER: &str = "cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXZ";

    fn file(r: &str, bytes: &[u8]) -> Fetched {
        Fetched {
            reference: r.into(),
            bytes: bytes.to_vec(),
            sha256: sha256_hex(bytes),
        }
    }

    fn doc(artifact_sha: &str, bytes: u64, mutate: impl FnOnce(&mut Value)) -> Vec<u8> {
        let mut v = json!({
            "id": ID, "schema": CHANGESET_SCHEMA, "intent": {"text": "t", "origin": "agent"},
            "operations": [
                {"opId": "op1", "tool": "dialogue.addNode",
                 "args": {"voice": {"artifact": format!("sha256:{artifact_sha}")}}},
                {"opId": "op2", "tool": "x.y", "dependsOn": ["op1"]}
            ],
            "artifacts": [{"sha256": artifact_sha, "name": "line.wav", "mediaType": "audio/wav", "bytes": bytes}]
        });
        mutate(&mut v);
        serde_json::to_vec(&v).unwrap()
    }

    fn cs(artifact_sha: &str, bytes: u64) -> Vec<u8> {
        doc(artifact_sha, bytes, |_| {})
    }

    fn rules(e: Evaluation) -> Vec<String> {
        match e {
            Evaluation::Invalid(d) => {
                assert!(d.iter().all(|x| x.code == "CandidateInvalid"), "{d:?}");
                d.iter().map(|x| x.rule().to_string()).collect()
            }
            other => panic!("accepted: {other:?}"),
        }
    }

    #[test]
    fn a_complete_output_is_valid() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let wav = b"RIFF....WAVE";
        let files = vec![
            file("r1", &cs(&sha256_hex(wav), wav.len() as u64)),
            file("r2", wav),
            file("r3", b"stray"),
        ];
        let Evaluation::Valid(v) = evaluate(&files, ID, &schema, ctx()) else {
            panic!("{:?}", evaluate(&files, ID, &schema, ctx()))
        };
        assert_eq!(v.artifacts.len(), 1);
        assert_eq!(v.artifacts[0].1, 1);
        assert_eq!(v.warnings.len(), 1);
        assert_eq!(v.warnings[0].rule(), "output_unlisted");
    }

    #[test]
    fn digest_size_id_and_reference_failures_are_reported() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let wav = b"RIFF....WAVE";
        let claimed = sha256_hex(b"something else");
        let r = rules(evaluate(
            &[file("r1", &cs(&claimed, 3)), file("r2", wav)],
            ID,
            &schema,
            ctx(),
        ));
        // Listed (so the reference resolves) but never delivered.
        assert_eq!(r, ["artifact_digest_mismatch"]);
        let r = rules(evaluate(
            &[file("r1", &cs(&sha256_hex(wav), 999)), file("r2", wav)],
            ID,
            &schema,
            ctx(),
        ));
        assert!(r.contains(&"artifact_size_mismatch".to_string()), "{r:?}");
        let r = rules(evaluate(
            &[file("r1", &cs(&sha256_hex(wav), 12)), file("r2", wav)],
            OTHER,
            &schema,
            ctx(),
        ));
        assert_eq!(r, ["changeset_id_mismatch"]);
        let Evaluation::Invalid(d) = evaluate(
            &[file("r1", &cs(&sha256_hex(wav), 12)), file("r2", wav)],
            OTHER,
            &schema,
            ctx(),
        ) else {
            panic!()
        };
        assert!(d[0].message.starts_with("changeset_id_mismatch: "), "{d:?}");
    }

    #[test]
    fn structural_rules_match_the_unity_validator() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let wav = b"RIFF....WAVE";
        let h = sha256_hex(wav);
        let n = wav.len() as u64;
        let check = |m: fn(&mut Value)| {
            rules(evaluate(
                &[file("r1", &doc(&h, n, m)), file("r2", wav)],
                ID,
                &schema,
                ctx(),
            ))
        };
        assert_eq!(
            check(|v| v["operations"][0]["dependsOn"] = json!(["op2"])),
            ["dependency_cycle"]
        );
        assert_eq!(
            check(|v| v["operations"][1] = json!({"opId": "op1", "tool": "x.y"})),
            ["duplicate_op_id"]
        );
        assert_eq!(
            check(|v| v["operations"][1]["dependsOn"] = json!(["op9"])),
            ["unknown_dependency"]
        );
        assert_eq!(
            check(|v| v["operations"][0]["args"] = json!({})),
            ["artifact_unused"]
        );
        let upper = check(|v| {
            let s = v["operations"][0]["args"]["voice"]["artifact"]
                .as_str()
                .unwrap()
                .to_ascii_uppercase()
                .replace("SHA256:", "sha256:");
            v["operations"][0]["args"]["voice"]["artifact"] = json!(s);
        });
        assert_eq!(upper, ["artifact_reference_invalid", "artifact_unused"]);
        // The operation located by op id (`where` is an op id or an AuthoringRef).
        let Evaluation::Invalid(d) = evaluate(
            &[
                file(
                    "r1",
                    &doc(&h, n, |v| {
                        v["operations"][1] = json!({"opId": "op1", "tool": "x.y"})
                    }),
                ),
                file("r2", wav),
            ],
            ID,
            &schema,
            ctx(),
        ) else {
            panic!()
        };
        assert_eq!(d[0].location, Some(json!("op1")));
    }

    fn codes(e: Evaluation) -> Vec<(String, String)> {
        match e {
            Evaluation::Invalid(d) => d
                .iter()
                .map(|x| (x.code.clone(), x.rule().to_string()))
                .collect(),
            other => panic!("accepted: {other:?}"),
        }
    }

    #[test]
    fn catalog_and_candidate_mode_rules() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let wav = b"RIFF....WAVE";
        let h = sha256_hex(wav);
        let n = wav.len() as u64;
        let run = |m: fn(&mut Value)| {
            codes(evaluate(
                &[file("r1", &doc(&h, n, m)), file("r2", wav)],
                ID,
                &schema,
                ctx(),
            ))
        };
        let c = |code: &str, rule: &str| (code.to_string(), rule.to_string());
        assert_eq!(
            run(|v| v["operations"][1]["tool"] = json!("x.unknown")),
            [c("UnknownTool", "UnknownTool")]
        );
        assert_eq!(
            run(|v| v["operations"][1]["args"] = json!({"bogus": 1})),
            [c("InvalidArgs", "InvalidArgs")]
        );
        // A required target and a required argument missing; then a disallowed kind and scope.
        assert_eq!(
            run(|v| v["operations"][1] = json!({"opId": "op2", "tool": "inventory.grantStarting"})),
            [
                c("InvalidArgs", "InvalidArgs"),
                c("InvalidArgs", "InvalidArgs")
            ]
        );
        assert_eq!(
            run(
                |v| v["operations"][1] = json!({"opId": "op2", "tool": "inventory.grantStarting",
                "target": {"kind": "Region", "authoringId": "r", "scope": "Prefab"}, "args": {"item": "i@1"}})
            ),
            [
                c("InvalidArgs", "InvalidArgs"),
                c("ScopeNotAllowed", "ScopeNotAllowed")
            ]
        );
        assert_eq!(
            run(|v| v["state"] = json!("Applied")),
            [c("CandidateInvalid", "candidate_mode")]
        );
        assert_eq!(
            run(|v| v["links"] = json!({"gameCoreOps": ["w:1"]})),
            [c("CandidateInvalid", "candidate_mode")]
        );
        assert_eq!(
            run(|v| v["outcomes"] = json!([{"opId": "op1", "status": "Applied"}])),
            [c("CandidateInvalid", "candidate_mode")]
        );
        // Null members are refused (with the schema's own complaint).
        let r = run(|v| v["artifacts"][0]["role"] = Value::Null);
        assert_eq!(r[0], c("CandidateInvalid", "null_member"));
        // `state: Candidate` is fine.
        let ok = doc(&h, n, |v| v["state"] = json!("Candidate"));
        assert!(matches!(
            evaluate(&[file("r1", &ok), file("r2", wav)], ID, &schema, ctx()),
            Evaluation::Valid(_)
        ));
        // A catalog that is gone, or that does not hash to the revision, is stale; no re-ask.
        let gone = CatalogContext {
            revision: &REVISION,
            catalog: None,
        };
        let Evaluation::Invalid(d) =
            evaluate(&[file("r1", &ok), file("r2", wav)], ID, &schema, gone)
        else {
            panic!()
        };
        assert_eq!(d[0].code, "StaleContext");
        assert!(!reaskable(&d));
        let other =
            json!({"schema": "gamecore.studio.toolcatalog/1", "objectTypes": [], "tools": []});
        let wrong = CatalogContext {
            revision: &REVISION,
            catalog: Some(&other),
        };
        let Evaluation::Invalid(d) =
            evaluate(&[file("r1", &ok), file("r2", wav)], ID, &schema, wrong)
        else {
            panic!()
        };
        assert_eq!(d[0].code, "StaleContext");
        assert_eq!(d[0].data.as_ref().unwrap()["expected"], json!(*REVISION));
    }

    #[test]
    fn missing_ambiguous_schema_and_clarification() {
        let schema = ChangeSetSchema::load(None).unwrap();
        assert_eq!(
            rules(evaluate(&[file("r", b"not json")], ID, &schema, ctx())),
            ["changeset_missing"]
        );
        let q = serde_json::to_vec(
            &json!({"status": "needs-clarification", "question": "Which lantern?"}),
        )
        .unwrap();
        assert!(matches!(
            evaluate(&[file("r", &q)], ID, &schema, ctx()),
            Evaluation::Clarification { ref question, .. } if question == "Which lantern?"
        ));
        let bad = serde_json::to_vec(&json!({"schema": CHANGESET_SCHEMA, "id": "cs_1"})).unwrap();
        let r = rules(evaluate(&[file("r", &bad)], ID, &schema, ctx()));
        assert!(
            !r.is_empty() && r.iter().all(|x| x == "schema_violation"),
            "{r:?}"
        );
        let one = cs(&sha256_hex(b"a"), 1);
        // The same change set delivered twice is still two documents.
        assert_eq!(
            rules(evaluate(
                &[file("r1", &one), file("r2", &one.clone()), file("r3", b"a")],
                ID,
                &schema,
                ctx(),
            )),
            ["ambiguous_output"]
        );
    }
}
