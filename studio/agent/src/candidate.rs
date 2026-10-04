//! Evaluation of a finished task's outputs into a candidate.
//!
//! The worker writes `/outputs/changeset.json` (a `gamecore.studio.changeset/1` document whose
//! `artifacts[]` lists every asset with its SHA-256) and the assets themselves; etos shares
//! them as pinned references on the task's final record. The companion fetches every
//! reference and identifies the documents by content (references carry no names on the agent
//! routes): the change set by its `schema`, a clarification by `status:
//! "needs-clarification"`. The candidate is valid when:
//!
//! 1. exactly one change set is present and it satisfies the JSON Schema;
//! 2. its `id` is the requested change-set id, op ids are unique and `dependsOn` resolve;
//! 3. every manifest entry's digest is the digest of a delivered file (and its `bytes`, when
//!    given, the file's size): a claimed asset that was not written, or whose bytes differ, is
//!    `artifact_digest_mismatch`;
//! 4. every `{"artifact": "sha256:..."}` in an operation's arguments is in the manifest.
//!
//! Delivered files not in the manifest are reported (`output_unlisted`) but not stored.

use std::collections::{BTreeSet, HashMap};

use serde_json::Value;

use crate::model::{ArtifactEntry, CHANGESET_SCHEMA, ChangeSet, Diagnostic};
use crate::schema::ChangeSetSchema;
use crate::util::normalize_sha256;

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
    /// The change set as written.
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

/// Evaluate the outputs of a task opened for `expected_id`.
pub fn evaluate(files: &[Fetched], expected_id: &str, schema: &ChangeSetSchema) -> Evaluation {
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
                return Evaluation::Invalid(vec![Diagnostic::new(
                    "clarification_invalid",
                    "the clarification has no `question`",
                )]);
            }
            return Evaluation::Clarification {
                question: question.to_string(),
                raw: c.clone(),
            };
        }
        let mut d = Diagnostic::new(
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
        return Evaluation::Invalid(vec![Diagnostic::new(
            "ambiguous_output",
            format!(
                "the outputs hold {} change sets and {} clarifications; exactly one document is allowed",
                changesets.len(),
                clarifications.len()
            ),
        )]);
    }
    let (cs_index, raw) = changesets.remove(0);
    let violations = schema.check(&raw);
    if !violations.is_empty() {
        return Evaluation::Invalid(violations);
    }
    let change_set: ChangeSet = match serde_json::from_value(raw.clone()) {
        Ok(c) => c,
        Err(e) => {
            return Evaluation::Invalid(vec![Diagnostic::new(
                "changeset_unreadable",
                format!("the change set does not fit the contract: {e}"),
            )]);
        }
    };
    let mut errors = Vec::new();
    if change_set.id != expected_id {
        errors.push(
            Diagnostic::new(
                "changeset_id_mismatch",
                format!(
                    "the change set says id {:?}, the request is {expected_id:?}",
                    change_set.id
                ),
            )
            .at("/id"),
        );
    }
    let mut op_ids = BTreeSet::new();
    for op in &change_set.operations {
        if !op_ids.insert(op.op_id.as_str()) {
            errors.push(
                Diagnostic::new("operation_invalid", format!("op id {:?} repeats", op.op_id))
                    .at(op.op_id.clone()),
            );
        }
    }
    for op in &change_set.operations {
        for dep in &op.depends_on {
            if !op_ids.contains(dep.as_str()) {
                errors.push(
                    Diagnostic::new(
                        "operation_invalid",
                        format!("op {:?} depends on unknown op {dep:?}", op.op_id),
                    )
                    .at(op.op_id.clone()),
                );
            }
        }
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
                Diagnostic::new(
                    "artifact_digest_invalid",
                    format!("{:?} is not a sha256", a.sha256),
                )
                .at(format!("/artifacts/{n}/sha256")),
            );
            continue;
        };
        let Some(&i) = by_digest.get(h.as_str()) else {
            let mut d = Diagnostic::new(
                "artifact_digest_mismatch",
                format!(
                    "no delivered output file has sha256 {h} (claimed for {:?})",
                    a.name
                ),
            )
            .at(format!("/artifacts/{n}"));
            d.hint = Some(
                "write the asset to /outputs and list the sha256 of the bytes actually written"
                    .into(),
            );
            errors.push(d);
            continue;
        };
        if let Some(b) = a.bytes
            && b != files[i].bytes.len() as u64
        {
            errors.push(
                Diagnostic::new(
                    "artifact_size_mismatch",
                    format!(
                        "{:?} claims {b} bytes, the delivered file has {}",
                        a.name,
                        files[i].bytes.len()
                    ),
                )
                .at(format!("/artifacts/{n}/bytes")),
            );
            continue;
        }
        if listed.insert(h.clone()) {
            let mut entry = a.clone();
            entry.sha256 = h;
            artifacts.push((entry, i));
        }
    }
    for op in &change_set.operations {
        let mut refs = Vec::new();
        collect_artifact_refs(&Value::Object(op.args.clone()), &mut refs);
        for r in refs {
            match normalize_sha256(&r) {
                Some(h) if listed.contains(&h) => {}
                _ => errors.push(
                    Diagnostic::new(
                        "artifact_unlisted_reference",
                        format!(
                            "op {:?} references artifact {r:?}, which is not in the manifest",
                            op.op_id
                        ),
                    )
                    .at(op.op_id.clone()),
                ),
            }
        }
    }
    if !errors.is_empty() {
        return Evaluation::Invalid(errors);
    }
    let warnings = files
        .iter()
        .enumerate()
        .filter(|(i, f)| *i != cs_index && !listed.contains(&f.sha256))
        .map(|(_, f)| {
            Diagnostic::new(
                "output_unlisted",
                format!(
                    "output {} (sha256 {}) is not in the manifest and was not kept",
                    f.reference, f.sha256
                ),
            )
        })
        .collect();
    Evaluation::Valid(Box::new(ValidCandidate {
        raw,
        change_set,
        artifacts,
        warnings,
    }))
}

/// Every string under an `artifact` key, at any depth.
fn collect_artifact_refs(v: &Value, out: &mut Vec<String>) {
    match v {
        Value::Object(map) => {
            for (k, child) in map {
                if k == "artifact"
                    && let Some(s) = child.as_str()
                {
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

    fn file(r: &str, bytes: &[u8]) -> Fetched {
        Fetched {
            reference: r.into(),
            bytes: bytes.to_vec(),
            sha256: sha256_hex(bytes),
        }
    }

    fn cs(artifact_sha: &str, bytes: u64) -> Vec<u8> {
        serde_json::to_vec(&json!({
            "id": "cs_1", "schema": CHANGESET_SCHEMA, "intent": {"text": "t"},
            "operations": [
                {"opId": "op1", "tool": "dialogue.addNode",
                 "args": {"voice": {"artifact": format!("sha256:{artifact_sha}")}}},
                {"opId": "op2", "tool": "x.y", "dependsOn": ["op1"]}
            ],
            "artifacts": [{"sha256": artifact_sha, "name": "line.wav", "mediaType": "audio/wav", "bytes": bytes}]
        }))
        .unwrap()
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
        let Evaluation::Valid(v) = evaluate(&files, "cs_1", &schema) else {
            panic!("{:?}", evaluate(&files, "cs_1", &schema))
        };
        assert_eq!(v.artifacts.len(), 1);
        assert_eq!(v.artifacts[0].1, 1);
        assert_eq!(v.warnings.len(), 1);
        assert_eq!(v.warnings[0].code, "output_unlisted");
    }

    #[test]
    fn digest_size_id_and_reference_failures_are_reported() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let wav = b"RIFF....WAVE";
        let claimed = sha256_hex(b"something else");
        let files = vec![file("r1", &cs(&claimed, 3)), file("r2", wav)];
        let Evaluation::Invalid(d) = evaluate(&files, "cs_1", &schema) else {
            panic!("accepted")
        };
        assert!(d.iter().any(|x| x.code == "artifact_digest_mismatch"));
        assert!(d.iter().any(|x| x.code == "artifact_unlisted_reference"));
        let files = vec![file("r1", &cs(&sha256_hex(wav), 999)), file("r2", wav)];
        let Evaluation::Invalid(d) = evaluate(&files, "cs_1", &schema) else {
            panic!("accepted")
        };
        assert!(d.iter().any(|x| x.code == "artifact_size_mismatch"));
        let files = vec![file("r1", &cs(&sha256_hex(wav), 12)), file("r2", wav)];
        let Evaluation::Invalid(d) = evaluate(&files, "cs_2", &schema) else {
            panic!("accepted")
        };
        assert_eq!(d[0].code, "changeset_id_mismatch");
    }

    #[test]
    fn missing_ambiguous_schema_and_clarification() {
        let schema = ChangeSetSchema::load(None).unwrap();
        let Evaluation::Invalid(d) = evaluate(&[file("r", b"not json")], "cs_1", &schema) else {
            panic!()
        };
        assert_eq!(d[0].code, "changeset_missing");
        let q = serde_json::to_vec(
            &json!({"status": "needs-clarification", "question": "Which lantern?"}),
        )
        .unwrap();
        assert!(matches!(
            evaluate(&[file("r", &q)], "cs_1", &schema),
            Evaluation::Clarification { ref question, .. } if question == "Which lantern?"
        ));
        let bad = serde_json::to_vec(&json!({"schema": CHANGESET_SCHEMA, "id": "cs_1"})).unwrap();
        let Evaluation::Invalid(d) = evaluate(&[file("r", &bad)], "cs_1", &schema) else {
            panic!()
        };
        assert!(d.iter().all(|x| x.code == "schema_violation"));
        let one = cs(&sha256_hex(b"a"), 1);
        let Evaluation::Invalid(d) = evaluate(
            &[file("r1", &one), file("r2", &one.clone()), file("r3", b"a")],
            "cs_1",
            &schema,
        ) else {
            panic!()
        };
        // The same change set delivered twice is still two documents.
        assert_eq!(d[0].code, "ambiguous_output");
    }
}
