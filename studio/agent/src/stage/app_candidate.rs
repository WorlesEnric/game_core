//! App-signed retained candidates. No worker, local path or verdict-file authority.
use crate::{
    api::{AppState, Caller},
    candidate::{self, CatalogContext, Evaluation, Fetched},
    error::{ApiError, ApiResult},
    ledger::{ArtifactRow, CandidateRow, NewRequest},
    schema::ChangeSetSchema,
    util::{canonical_json, now_ms, sha256_hex},
};
use axum::{
    Extension, Json,
    extract::State,
    http::{HeaderMap, StatusCode},
    response::{IntoResponse, Response},
};
use base64::Engine as _;
use serde::Deserialize;
use serde_json::{Value, json};

/// The signed bytes are UTF-8 JSON (not reserialized), domain-separated from other uses.
#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SignedCandidate {
    /// Base64 UTF-8 payload containing app, request, changeSet, toolCatalog and files.
    payload_base64: String,
    /// Hex HMAC-SHA256(app-key, "gamecore.stage.app-candidate/1\n" + payload bytes).
    signature: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Payload {
    app: String,
    request: Value,
    change_set: Value,
    tool_catalog: Value,
    files: Vec<Upload>,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Upload {
    bytes_base64: String,
}

/// Verify both node authority and a signature over the exact submitted bytes. The key is
/// transient, never persisted/logged; the node's proxy normally strips Authorization.
pub async fn submit(
    State(s): State<AppState>,
    Extension(Caller(owner)): Extension<Caller>,
    headers: HeaderMap,
    Json(envelope): Json<SignedCandidate>,
) -> ApiResult<Response> {
    let key = headers
        .get("X-GameCore-Stage-Key")
        .and_then(|v| v.to_str().ok())
        .filter(|v| !v.is_empty())
        .ok_or_else(|| ApiError::forbidden("app signing key required"))?;
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(&envelope.payload_base64)
        .map_err(|_| ApiError::bad_request("payloadBase64 is invalid"))?;
    let signature = hex::decode(&envelope.signature)
        .map_err(|_| ApiError::forbidden("invalid app signature"))?;
    let mut message = b"gamecore.stage.app-candidate/1\n".to_vec();
    message.extend_from_slice(&bytes);
    let expected = super::signing::hmac(key.as_bytes(), &message);
    if signature.len() != expected.len()
        || signature
            .iter()
            .zip(expected)
            .fold(0u8, |diff, (a, b)| diff | (a ^ b))
            != 0
    {
        return Err(ApiError::forbidden("invalid app signature"));
    }
    let value: Value = serde_json::from_slice(&bytes)
        .map_err(|_| ApiError::bad_request("invalid signed candidate payload"))?;
    if let Some(path) = crate::util::first_null(&value) {
        return Err(ApiError::bad_request(format!("null at {path}")));
    }
    let payload: Payload = serde_json::from_value(value)
        .map_err(|_| ApiError::bad_request("invalid signed candidate payload"))?;
    let identity: Value =
        serde_json::from_str(&owner).map_err(|_| ApiError::forbidden("invalid authority"))?;
    if identity[0] != payload.app || identity[1] != payload.request["projectId"] {
        return Err(ApiError::forbidden(
            "signed app/project must match proxy authority",
        ));
    }
    // An attacker can sign with any string: ask the trusted node to authenticate this key
    // for this companion, then compare its attested app. No secrets are copied to state.
    let client = reqwest::Client::builder()
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .timeout(std::time::Duration::from_secs(10))
        .build()
        .map_err(|_| ApiError::internal("app verifier unavailable"))?;
    let verified = client
        .get(format!(
            "{}/api/v1/agents/{}/http/v1/hello",
            s.agent.client().url(),
            s.cfg.agent
        ))
        .bearer_auth(key)
        .send()
        .await
        .map_err(|_| ApiError::forbidden("node could not authenticate the app signing key"))?;
    if verified.status() != StatusCode::OK {
        return Err(ApiError::forbidden("node refused the app signing key"));
    }
    let hello: Value = verified
        .json()
        .await
        .map_err(|_| ApiError::forbidden("invalid node app attestation"))?;
    if hello["app"] != payload.app {
        return Err(ApiError::forbidden("signing key belongs to another app"));
    }
    let answer = s.stage.retain_app_payload(payload, &owner)?;
    Ok((answer.status, Json(answer.body)).into_response())
}

impl super::StageRunner {
    fn retain_app_payload(
        self: &std::sync::Arc<Self>,
        payload: Payload,
        owner: &str,
    ) -> ApiResult<super::StageAnswer> {
        let req: super::StageRunRequest = serde_json::from_value(payload.request.clone())
            .map_err(|e| ApiError::bad_request(e.to_string()))?;
        let project = req
            .project_id
            .as_deref()
            .ok_or_else(|| ApiError::bad_request("projectId required"))?;
        self.registered_project(project)?;
        if req.source_project.is_some() || req.action.as_deref().is_some_and(|v| v != "stage") {
            return Err(ApiError::bad_request(
                "app candidate must request stage without sourceProject",
            ));
        }
        let catalog_revision = req
            .catalog_revision
            .as_deref()
            .ok_or_else(|| ApiError::bad_request("catalogRevision required"))?;
        let catalog_schema =
            crate::schema::Schema::builtin(crate::schema::TOOL_CATALOG, "tool-catalog")
                .map_err(ApiError::internal)?;
        if !catalog_schema.check(&payload.tool_catalog).is_empty() {
            return Err(ApiError::bad_request("invalid tool catalog"));
        }
        let raw = serde_json::to_vec(&payload.change_set)
            .map_err(|e| ApiError::bad_request(e.to_string()))?;
        let mut files = vec![Fetched {
            reference: "app:changeset".into(),
            sha256: sha256_hex(&raw),
            bytes: raw,
        }];
        for (i, file) in payload.files.iter().enumerate() {
            let bytes = base64::engine::general_purpose::STANDARD
                .decode(&file.bytes_base64)
                .map_err(|_| ApiError::bad_request("invalid file base64"))?;
            files.push(Fetched {
                reference: format!("app:{i}"),
                sha256: sha256_hex(&bytes),
                bytes,
            });
        }
        let schema = ChangeSetSchema::load(None).map_err(ApiError::internal)?;
        let validated = match candidate::evaluate(
            &files,
            &req.change_set_id,
            &schema,
            CatalogContext {
                revision: catalog_revision,
                catalog: Some(&payload.tool_catalog),
            },
        ) {
            Evaluation::Valid(v) => v,
            Evaluation::Invalid(d) => {
                return Err(ApiError::new(
                    StatusCode::BAD_REQUEST,
                    "candidate_invalid",
                    "app candidate failed validation",
                )
                .with_data(json!({"diagnostics":d})));
            }
            _ => return Err(ApiError::bad_request("a change set is required")),
        };
        if validated
            .change_set
            .operations
            .iter()
            .any(|op| op.tool != "mechanism.propose")
        {
            return Err(ApiError::bad_request(
                "app staging accepts only mechanism.propose operations",
            ));
        }
        let mut artifacts = Vec::new();
        let mut artifact_rows = Vec::new();
        for (artifact, index) in &validated.artifacts {
            let (sha, path) = self
                .store
                .put(&files[*index].bytes, Some(&artifact.sha256))
                .map_err(|e| ApiError::internal(e.to_string()))?;
            let producer = json!({"origin":"app", "changeSetId":req.change_set_id});
            let bytes = files[*index].bytes.len() as u64;
            artifacts.push(crate::model::StoredArtifact {
                sha256: sha.clone(),
                name: artifact.name.clone(),
                media_type: artifact.media_type.clone(),
                bytes,
                producer: producer.clone(),
                role: artifact.role.clone(),
                url: format!("/v1/artifacts/{sha}"),
            });
            artifact_rows.push(ArtifactRow {
                sha256: sha,
                path: path.display().to_string(),
                bytes,
                media_type: artifact.media_type.clone(),
                name: artifact.name.clone(),
                role: artifact.role.clone(),
                producer,
                change_set_id: Some(req.change_set_id.clone()),
            });
        }
        let candidate_digest = sha256_hex(canonical_json(&validated.raw).as_bytes());
        let body = json!({"origin":"app","toolCatalogRevision":catalog_revision,"candidateDigest":candidate_digest});
        let row = CandidateRow {
            change_set_id: req.change_set_id.clone(),
            request_id: req.change_set_id.clone(),
            task_id: String::new(),
            attempt: 0,
            change_set: validated.raw,
            artifacts: json!(artifacts),
            diagnostics: json!(validated.warnings),
            received_at: now_ms(),
        };
        self.ledger.retain_app_candidate(
            &NewRequest {
                change_set_id: req.change_set_id.clone(),
                digest: sha256_hex(canonical_json(&body).as_bytes()),
                body,
                app: owner.into(),
                worker: "app".into(),
                etos_request_id: String::new(),
                topic: String::new(),
            },
            &row,
        )?;
        for artifact in &artifact_rows {
            self.ledger.put_artifact(artifact)?;
            self.ledger.grant("artifact", &artifact.sha256, owner)?;
        }
        self.request_owned(payload.request, owner)
    }
}
