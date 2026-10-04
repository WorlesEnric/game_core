//! Media operations (04 §5) and the provider status of `/v1/hello`.
//!
//! `POST /v1/ops/generate {op, spec, max_cost_usd, changeSetId}` runs
//! `POST /ops/generate.image | tts | generate.3d | describe` through
//! `client.ops().call(...)` (not the SDK's `Ops::generate`, which posts the wrong path), with
//! an etops idempotency `key` derived from the change set and the spec (a lost answer is
//! looked up by etops, never generated twice). Every operation carries a cost ceiling: the
//! call's `max_cost_usd`, else the configured `ops_max_cost_usd`; with neither the call is
//! refused. Produced files (`refs`) are fetched, checked against the digest the node reports
//! (when it reports one), stored under the node's media type (else one guessed from the
//! name); refusals (`not_configured`, `budget_exhausted`, ...) pass through unchanged.
//!
//! Provider status comes from the free `status` operation (`{providers: {op: [names]}}`),
//! cached for `hello_cache_s`; voice status is what the last realtime session learned
//! (`unknown` before the first one). Hello never generates anything.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use etos_sdk::Client;
use serde_json::{Map, Value, json};

use crate::error::{ApiError, ApiResult};
use crate::events::EventHub;
use crate::index::Indexer;
use crate::ledger::{ArtifactRow, Ledger};
use crate::model::{GenerateRequest, GenerateResponse, StoredArtifact};
use crate::store::ArtifactStore;
use crate::util::{canonical_json, media_type_for, normalize_sha256, now_ms, sha256_hex};

/// Live: a provider is configured. Not configured: none is. Blocked: the agent may not use
/// it (grant, budget). Unknown: not determined yet.
pub mod status {
    /// A provider is configured on the node.
    pub const LIVE: &str = "live";
    /// No provider is configured.
    pub const NOT_CONFIGURED: &str = "not_configured";
    /// The agent may not use it (grant or budget).
    pub const BLOCKED: &str = "blocked";
    /// Not known yet.
    pub const UNKNOWN: &str = "unknown";
}

/// The etos operation of a Studio op name.
pub fn etos_op(op: &str) -> Option<&'static str> {
    Some(match op {
        "image" => "generate.image",
        "tts" => "tts",
        "3d" => "generate.3d",
        "describe" => "describe",
        _ => return None,
    })
}

/// A cached provider status: when read (monotonic and ms), status by capability.
type StatusCache = Option<(Instant, i64, BTreeMap<String, String>)>;

/// Media operations.
pub struct MediaOps {
    client: Client,
    ledger: Arc<Ledger>,
    store: ArtifactStore,
    hub: EventHub,
    indexer: Arc<Indexer>,
    ttl: Duration,
    default_ceiling: Option<f64>,
    cache: Mutex<StatusCache>,
    voice: Mutex<String>,
}

impl std::fmt::Debug for MediaOps {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("MediaOps").finish_non_exhaustive()
    }
}

fn blocked_code(code: &str) -> bool {
    matches!(
        code,
        "forbidden" | "not_granted" | "not_allowed" | "budget_exhausted" | "unauthorized"
    )
}

impl MediaOps {
    /// Media operations over the agent's client.
    pub fn new(
        client: Client,
        ledger: Arc<Ledger>,
        store: ArtifactStore,
        hub: EventHub,
        indexer: Arc<Indexer>,
        hello_cache_s: u64,
        default_ceiling: Option<f64>,
    ) -> MediaOps {
        MediaOps {
            client,
            ledger,
            store,
            hub,
            indexer,
            ttl: Duration::from_secs(hello_cache_s),
            default_ceiling,
            cache: Mutex::new(None),
            voice: Mutex::new(status::UNKNOWN.to_string()),
        }
    }

    /// Record what a realtime session learned about the voice provider.
    pub fn set_voice_status(&self, s: &str) {
        if let Ok(mut v) = self.voice.lock() {
            *v = s.to_string();
        }
    }

    /// `image`, `tts`, `3d`, `describe`, `voice` → status, and when it was read.
    pub async fn providers(&self) -> (BTreeMap<String, String>, Option<i64>) {
        let voice = self
            .voice
            .lock()
            .map(|v| v.clone())
            .unwrap_or_else(|_| status::UNKNOWN.into());
        if let Ok(c) = self.cache.lock()
            && let Some((when, at, map)) = c.as_ref()
            && when.elapsed() < self.ttl
        {
            let mut m = map.clone();
            m.insert("voice".into(), voice);
            return (m, Some(*at));
        }
        let ops = [
            ("image", "image"),
            ("tts", "tts"),
            ("3d", "3d"),
            ("describe", "describe"),
        ];
        let mut m = BTreeMap::new();
        let mut cacheable = true;
        match self.client.ops().call("status", json!({})).await {
            Ok(answer) => {
                for (name, op) in ops {
                    let configured = answer
                        .pointer(&format!("/providers/{op}"))
                        .and_then(Value::as_array)
                        .is_some_and(|a| !a.is_empty());
                    m.insert(
                        name.to_string(),
                        if configured {
                            status::LIVE
                        } else {
                            status::NOT_CONFIGURED
                        }
                        .to_string(),
                    );
                }
            }
            Err(e) => {
                let s = if blocked_code(e.code()) {
                    status::BLOCKED
                } else if matches!(e.code(), "not_available" | "not_configured") {
                    status::NOT_CONFIGURED
                } else {
                    cacheable = false;
                    tracing::warn!(error = %e, "provider status unavailable");
                    status::UNKNOWN
                };
                for (name, _) in ops {
                    m.insert(name.to_string(), s.to_string());
                }
            }
        }
        let at = now_ms();
        if cacheable && let Ok(mut c) = self.cache.lock() {
            *c = Some((Instant::now(), at, m.clone()));
        }
        m.insert("voice".into(), voice);
        (m, Some(at))
    }

    /// `POST /v1/ops/generate`.
    pub async fn generate(&self, req: GenerateRequest) -> ApiResult<GenerateResponse> {
        let op = etos_op(&req.op).ok_or_else(|| {
            ApiError::bad_request(format!(
                "op {:?} is not one of image, tts, 3d, describe",
                req.op
            ))
        })?;
        let mut input: Map<String, Value> = req.spec.clone();
        if input.contains_key("output") {
            return Err(ApiError::bad_request(
                "`output` is chosen by the node, not the caller",
            ));
        }
        let max = match req.max_cost_usd.or(self.default_ceiling) {
            Some(m) if m.is_finite() && m >= 0.0 => m,
            Some(_) => {
                return Err(ApiError::bad_request(
                    "max_cost_usd is a non-negative number",
                ));
            }
            None => {
                return Err(ApiError::bad_request("max_cost_usd is required")
                    .with_hint("send a cost ceiling in USD, or configure ops_max_cost_usd"));
            }
        };
        input.insert("max_cost_usd".into(), json!(max));
        if op == "describe" {
            return self.describe(&req, input, max).await;
        }
        let key = match input.get("key").and_then(Value::as_str) {
            Some(k) => k.to_string(),
            None => {
                let basis =
                    json!({"cs": req.change_set_id, "op": op, "spec": req.spec, "max": max});
                let k = format!(
                    "gc-{}",
                    &sha256_hex(canonical_json(&basis).as_bytes())[..40]
                );
                input.insert("key".into(), json!(k));
                k
            }
        };
        tracing::info!(op, key = %key, "running a media operation");
        let answer = self.client.ops().call(op, Value::Object(input)).await?;
        let provider = answer
            .get("provider")
            .and_then(Value::as_str)
            .map(str::to_string);
        let job = answer.get("job_id").cloned().unwrap_or(Value::Null);
        let mut artifacts = Vec::new();
        for r in answer
            .get("refs")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default()
        {
            let field = |k: &[&str]| {
                k.iter()
                    .find_map(|k| r.get(*k).and_then(Value::as_str))
                    .map(str::to_string)
            };
            let (id, name) = match &r {
                Value::String(s) => (s.clone(), s.clone()),
                Value::Object(_) => (
                    field(&["id"]).unwrap_or_default(),
                    field(&["name"]).unwrap_or_default(),
                ),
                _ => continue,
            };
            if id.is_empty() {
                continue;
            }
            let reported = field(&["digest", "sha256"]).and_then(|d| normalize_sha256(&d));
            let bytes = self.client.files().get(&id).await?;
            let (sha, path) = self.store.put(&bytes, reported.as_deref()).map_err(|e| {
                ApiError::new(
                    axum::http::StatusCode::BAD_GATEWAY,
                    "protocol",
                    format!("output {id} does not match the digest the node reported: {e}"),
                )
            })?;
            let media = field(&["media_type", "mediaType"])
                .filter(|m| !m.is_empty())
                .unwrap_or_else(|| media_type_for(&name).to_string());
            let producer = json!({"op": op, "provider": provider, "jobId": job, "key": key,
                                  "etosRef": id, "changeSetId": req.change_set_id});
            self.ledger.put_artifact(&ArtifactRow {
                sha256: sha.clone(),
                path: path.display().to_string(),
                bytes: bytes.len() as u64,
                media_type: media.clone(),
                name: name.clone(),
                role: None,
                producer: producer.clone(),
                change_set_id: req.change_set_id.clone(),
            })?;
            let mut a = Map::new();
            a.insert("name".into(), json!(name));
            a.insert("media_type".into(), json!(media));
            a.insert("producer".into(), producer.clone());
            if let Some(cs) = &req.change_set_id {
                a.insert("changeset".into(), json!(cs));
            }
            self.indexer.record("gc_asset", &sha, a);
            artifacts.push(StoredArtifact {
                url: format!("/v1/artifacts/{sha}"),
                sha256: sha,
                name,
                media_type: media,
                bytes: bytes.len() as u64,
                producer,
                role: None,
            });
        }
        let response = GenerateResponse {
            op: req.op.clone(),
            etos_op: op.to_string(),
            provider,
            state: answer.get("state").cloned().filter(|v| !v.is_null()),
            max_cost_usd: max,
            artifacts,
            text: None,
            key: Some(key),
        };
        if let Err(e) = self.hub.emit(
            "asset",
            req.change_set_id.as_deref(),
            &serde_json::to_value(&response).unwrap_or(Value::Null),
        ) {
            tracing::warn!(error = %e, "cannot record the asset event");
        }
        Ok(response)
    }

    async fn describe(
        &self,
        req: &GenerateRequest,
        mut input: Map<String, Value>,
        max: f64,
    ) -> ApiResult<GenerateResponse> {
        if let Some(sha) = input.remove("artifact") {
            let sha = sha
                .as_str()
                .and_then(normalize_sha256)
                .ok_or_else(|| ApiError::bad_request("`artifact` is a sha256 digest"))?;
            let bytes = self
                .store
                .get(&sha)
                .map_err(|e| ApiError::internal(e.to_string()))?
                .ok_or_else(|| ApiError::not_found(format!("no stored artifact {sha}")))?;
            let (media, name) = self
                .ledger
                .artifact(&sha)
                .map(|a| (a.media_type, a.name))
                .unwrap_or_else(|_| ("application/octet-stream".into(), sha.clone()));
            let info = self.client.files().put(&name, &media, &bytes).await?;
            input.insert("input".into(), json!(info.reference.id));
        }
        if !input.get("input").is_some_and(Value::is_string) {
            return Err(ApiError::bad_request(
                "describe needs `artifact` (a stored digest) or `input` (an etos reference)",
            ));
        }
        let answer = self
            .client
            .ops()
            .call("describe", Value::Object(input))
            .await?;
        Ok(GenerateResponse {
            op: req.op.clone(),
            etos_op: "describe".into(),
            provider: answer
                .get("provider")
                .and_then(Value::as_str)
                .map(str::to_string),
            state: Some(json!("succeeded")),
            max_cost_usd: max,
            artifacts: Vec::new(),
            text: answer
                .get("text")
                .and_then(Value::as_str)
                .map(str::to_string),
            key: None,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn studio_ops_map_to_etos_ops() {
        assert_eq!(etos_op("image"), Some("generate.image"));
        assert_eq!(etos_op("tts"), Some("tts"));
        assert_eq!(etos_op("3d"), Some("generate.3d"));
        assert_eq!(etos_op("describe"), Some("describe"));
        assert_eq!(etos_op("generate"), None);
        assert!(blocked_code("budget_exhausted"));
        assert!(!blocked_code("not_configured"));
    }
}
