//! The contract JSON Schemas, built into the binary: byte-for-byte copies of P0.3's generated
//! `docs/studio/schemas/*.schema.json` in `schemas/` (`build.rs` warns when a copy differs).
//!
//! - `change-set.schema.json` checks a worker's change set ([`crate::candidate`]);
//!   `schema = <path>` in `config.toml` (or `GAMECORE_STUDIO_SCHEMA`) loads another file;
//! - `selection-snapshot.schema.json`, `semantic-index.schema.json` and
//!   `tool-catalog.schema.json` check what `POST /v1/requests` carries.
//!
//! Remote `$ref`s are not resolved (the generated schemas have none).

use std::path::Path;

use serde_json::Value;

use crate::model::Diagnostic;

/// The built-in change-set schema text.
pub const VENDORED: &str = include_str!("../schemas/change-set.schema.json");
/// The built-in selection-snapshot schema text.
pub const SELECTION: &str = include_str!("../schemas/selection-snapshot.schema.json");
/// The built-in semantic-index schema text.
pub const SEMANTIC_INDEX: &str = include_str!("../schemas/semantic-index.schema.json");
/// The built-in tool-catalog schema text.
pub const TOOL_CATALOG: &str = include_str!("../schemas/tool-catalog.schema.json");

/// A compiled JSON Schema.
pub struct Schema {
    validator: jsonschema::Validator,
    source: String,
}

/// The change-set schema (a [`Schema`]).
pub type ChangeSetSchema = Schema;

impl std::fmt::Debug for Schema {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Schema")
            .field("source", &self.source)
            .finish_non_exhaustive()
    }
}

impl Schema {
    /// Compile a schema value; `source` names it in messages.
    pub fn compile(schema: &Value, source: &str) -> Result<Schema, String> {
        let validator = jsonschema::validator_for(schema)
            .map_err(|e| format!("the schema {source} does not compile: {e}"))?;
        Ok(Schema {
            validator,
            source: source.to_string(),
        })
    }

    /// A built-in schema from its text.
    pub fn builtin(text: &str, name: &str) -> Result<Schema, String> {
        let v: Value = serde_json::from_str(text)
            .map_err(|e| format!("the built-in schema {name} is not JSON: {e}"))?;
        Schema::compile(&v, &format!("schemas/{name} (built in, P0.3 copy)"))
    }

    /// The built-in change-set schema, or the file at `path`.
    pub fn load(path: Option<&Path>) -> Result<Schema, String> {
        match path {
            None => Schema::builtin(VENDORED, "change-set.schema.json"),
            Some(p) => {
                let text = std::fs::read_to_string(p)
                    .map_err(|e| format!("cannot read {}: {e}", p.display()))?;
                let v: Value = serde_json::from_str(&text)
                    .map_err(|e| format!("{} is not JSON: {e}", p.display()))?;
                Schema::compile(&v, &p.display().to_string())
            }
        }
    }

    /// Where the schema came from.
    pub fn source(&self) -> &str {
        &self.source
    }

    /// Violations of `instance` as `code` diagnostics naming `rule`, with the JSON pointer
    /// in the message; empty when valid. At most 50 are reported.
    pub fn findings(&self, instance: &Value, code: &str, rule: &str) -> Vec<Diagnostic> {
        self.validator
            .iter_errors(instance)
            .take(50)
            .map(|e| {
                let at = e.instance_path.to_string();
                Diagnostic::new(code, format!("{rule}: {e}")).at_path(if at.is_empty() {
                    "/".to_string()
                } else {
                    at
                })
            })
            .collect()
    }

    /// Change-set violations (`CandidateInvalid` / `schema_violation`).
    pub fn check(&self, instance: &Value) -> Vec<Diagnostic> {
        self.findings(
            instance,
            crate::model::CANDIDATE_INVALID,
            "schema_violation",
        )
    }
}

/// The request-side schemas of `POST /v1/requests`.
#[derive(Debug)]
pub struct RequestSchemas {
    /// `selection-snapshot.schema.json`.
    pub selection: Schema,
    /// `semantic-index.schema.json` (the context slice).
    pub slice: Schema,
    /// `tool-catalog.schema.json`.
    pub catalog: Schema,
}

impl RequestSchemas {
    /// The built-in request schemas.
    pub fn builtin() -> Result<RequestSchemas, String> {
        Ok(RequestSchemas {
            selection: Schema::builtin(SELECTION, "selection-snapshot.schema.json")?,
            slice: Schema::builtin(SEMANTIC_INDEX, "semantic-index.schema.json")?,
            catalog: Schema::builtin(TOOL_CATALOG, "tool-catalog.schema.json")?,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn request_schemas_compile_and_check() {
        let r = RequestSchemas::builtin().unwrap();
        let sel = json!({"id": "sel_01J9ZQ3K4M5N6P7Q8R9S0TVWXY", "mode": "Edit", "indexRevision": 1, "targets": []});
        assert!(
            r.selection
                .findings(&sel, "InvalidArgs", "selection")
                .is_empty()
        );
        let bad = json!({"id": "sel_1", "mode": "Edit", "indexRevision": 1, "targets": []});
        let d = r.selection.findings(&bad, "InvalidArgs", "selection");
        assert!(
            !d.is_empty() && d[0].message.starts_with("selection: "),
            "{d:?}"
        );
        let slice = json!({"project": "p", "revision": 1, "nodes": [{"ref": {"kind": "Entity", "authoringId": "e"}, "type": "npc.definition"}]});
        assert!(r.slice.findings(&slice, "InvalidArgs", "slice").is_empty());
        let cat =
            json!({"schema": "gamecore.studio.toolcatalog/1", "objectTypes": [], "tools": []});
        assert!(
            r.catalog
                .findings(&cat, "InvalidArgs", "catalog")
                .is_empty(),
            "{:?}",
            r.catalog.findings(&cat, "InvalidArgs", "catalog")
        );
    }

    #[test]
    fn vendored_schema_accepts_the_contract_example_and_rejects_garbage() {
        let s = ChangeSetSchema::load(None).unwrap();
        let ok = json!({
            "id": "cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXY", "schema": "gamecore.studio.changeset/1",
            "intent": {"text": "lantern", "origin": "agent"},
            "selection": {"id": "sel_01J9ZQ3K4M5N6P7Q8R9S0TVWXY", "mode": "Edit", "indexRevision": 3,
                          "targets": [{"kind": "Entity", "authoringId": "7f1c"}]},
            "operations": [{"opId": "op1", "tool": "inventory.grantStarting",
                            "target": {"kind": "Entity", "authoringId": "7f1c"},
                            "args": {"item": "item.lantern@2", "count": 1},
                            "preconditions": "none", "applyRequirement": "Live"}],
            "artifacts": [{"sha256": "a".repeat(64), "name": "x.wav", "mediaType": "audio/wav", "bytes": 4}],
            "requirements": {"max": "Live", "worldRebuild": false, "compile": false, "build": false}
        });
        assert!(s.check(&ok).is_empty(), "{:?}", s.check(&ok));
        let bad = json!({"id": "cs_1", "schema": "other", "operations": [{"tool": 3}]});
        // P0.3's contract: a ULID id, lowercase bare digests, no unknown fields.
        for m in [
            json!({"id": "cs_1"}),
            json!({"artifacts": [{"sha256": format!("sha256:{}", "a".repeat(64)), "mediaType": "x", "bytes": 1}]}),
            json!({"extra": true}),
        ] {
            let mut v = ok.clone();
            for (k, x) in m.as_object().unwrap() {
                v[k] = x.clone();
            }
            assert!(!s.check(&v).is_empty(), "accepted {m}");
        }
        let d = s.check(&bad);
        assert!(d.len() >= 3, "{d:?}");
        assert!(d.iter().all(|x| x.rule() == "schema_violation"));
    }
}
