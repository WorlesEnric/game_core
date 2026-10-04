//! The change-set JSON Schema. By default the vendored copy in `schemas/` (built into the
//! binary); `schema = <path>` in `config.toml` (or `GAMECORE_STUDIO_SCHEMA`) loads the
//! generated one from `docs/studio/schemas/` instead. Remote `$ref`s are not resolved.

use std::path::Path;

use serde_json::Value;

use crate::model::Diagnostic;

/// The vendored schema text.
pub const VENDORED: &str = include_str!("../schemas/change-set.schema.json");

/// A compiled change-set schema.
pub struct ChangeSetSchema {
    validator: jsonschema::Validator,
    source: String,
}

impl std::fmt::Debug for ChangeSetSchema {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ChangeSetSchema")
            .field("source", &self.source)
            .finish_non_exhaustive()
    }
}

impl ChangeSetSchema {
    /// Compile a schema value; `source` names it in messages.
    pub fn compile(schema: &Value, source: &str) -> Result<ChangeSetSchema, String> {
        let validator = jsonschema::validator_for(schema)
            .map_err(|e| format!("the change-set schema {source} does not compile: {e}"))?;
        Ok(ChangeSetSchema {
            validator,
            source: source.to_string(),
        })
    }

    /// The vendored schema, or the file at `path`.
    pub fn load(path: Option<&Path>) -> Result<ChangeSetSchema, String> {
        match path {
            None => {
                let v: Value = serde_json::from_str(VENDORED)
                    .map_err(|e| format!("the vendored schema is not JSON: {e}"))?;
                ChangeSetSchema::compile(&v, "schemas/change-set.schema.json (vendored)")
            }
            Some(p) => {
                let text = std::fs::read_to_string(p)
                    .map_err(|e| format!("cannot read {}: {e}", p.display()))?;
                let v: Value = serde_json::from_str(&text)
                    .map_err(|e| format!("{} is not JSON: {e}", p.display()))?;
                ChangeSetSchema::compile(&v, &p.display().to_string())
            }
        }
    }

    /// Where the schema came from.
    pub fn source(&self) -> &str {
        &self.source
    }

    /// Schema violations of `instance` as diagnostics (`schema_violation`, located by JSON
    /// pointer); empty when valid. At most 50 are reported.
    pub fn check(&self, instance: &Value) -> Vec<Diagnostic> {
        self.validator
            .iter_errors(instance)
            .take(50)
            .map(|e| {
                let at = e.instance_path.to_string();
                Diagnostic::new("schema_violation", e.to_string()).at(if at.is_empty() {
                    "/".to_string()
                } else {
                    at
                })
            })
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn vendored_schema_accepts_the_contract_example_and_rejects_garbage() {
        let s = ChangeSetSchema::load(None).unwrap();
        let ok = json!({
            "id": "cs_1", "schema": "gamecore.studio.changeset/1",
            "intent": {"text": "lantern", "origin": "agent"},
            "operations": [{"opId": "op1", "tool": "inventory.grantStarting",
                            "target": {"kind": "Entity", "authoringId": "7f1c"},
                            "args": {"item": "item.lantern@2", "count": 1}}],
            "artifacts": [{"sha256": "a".repeat(64), "name": "x.wav", "mediaType": "audio/wav"}]
        });
        assert!(s.check(&ok).is_empty(), "{:?}", s.check(&ok));
        let bad = json!({"id": "cs_1", "schema": "other", "operations": [{"tool": 3}]});
        let d = s.check(&bad);
        assert!(d.len() >= 3, "{d:?}");
        assert!(d.iter().all(|x| x.code == "schema_violation"));
    }
}
