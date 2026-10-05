//! The staging lane (P2.4; 03 §8, 04 §6): generated code is staged and validated in an
//! isolated Unity project + dotnet workspace (a *slot*) before it can affect the live editor.
//!
//! HTTP staging is candidate-only: `{changeSetId, projectId, sourceRevision, catalogRevision}`.
//! Project paths come from operator configuration. The authenticated app/project owns jobs,
//! artifacts and slot roots. Only full passing service records receive an installation HMAC.
//! Legacy packageRef extraction is refused. See evidence/PACKET.md for the wire contract.
//!
//! Candidate execution defaults to Docker confinement with no network or live project mount.
//! The operator CLI is `gamecore-studio stage run|gc|discard|scan`; every log is redacted.

pub mod cli;
pub mod env;
pub mod pipeline;
pub mod sandbox;
pub mod scan;
pub mod signing;
pub mod slot;
pub mod verdict;

use std::collections::BTreeSet;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use axum::http::StatusCode;
use serde::Deserialize;
use serde_json::{Value, json};

pub use env::stage_env;

use crate::config::StageConfig;
use crate::error::{ApiError, ApiResult, STAGE_FAILED};
use crate::events::EventHub;
use crate::ledger::{Ledger, LedgerError};
use crate::model::StageJobView;
use crate::redact::redact;
use crate::store::ArtifactStore;
use crate::util::{new_id, normalize_sha256, now_ms, valid_change_set_id};

/// The last JSON object printed on its own line (or the whole output as one object).
pub fn parse_verdict(stdout: &str) -> Option<Value> {
    stdout
        .lines()
        .rev()
        .filter_map(|l| serde_json::from_str::<Value>(l.trim()).ok())
        .find(Value::is_object)
        .or_else(|| {
            serde_json::from_str::<Value>(stdout.trim())
                .ok()
                .filter(Value::is_object)
        })
}

/// `POST /v1/stage` in its staging-lane shape.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct StageRunRequest {
    /// Stable project SHA-256, identical to X-GameCore-Project.
    #[serde(default)]
    pub project_id: Option<String>,
    /// Expected source revision, checked against the trusted checkout.
    #[serde(default)]
    pub source_revision: Option<String>,
    /// Catalog revision bound to the validated candidate request.
    #[serde(default)]
    pub catalog_revision: Option<String>,
    /// The change set to stage (a candidate the companion validated).
    pub change_set_id: String,
    /// The slot (default: the slot holding the change set, else `cs-<ulid>`).
    #[serde(default)]
    pub slot: Option<String>,
    /// Steps to run (default all; a partial verdict never passes).
    #[serde(default)]
    pub steps: Option<Vec<String>>,
    /// The source Unity project (default `GAMECORE_STAGE_SOURCE_PROJECT`, else the
    /// repository's `games/hollowmere`).
    #[serde(default)]
    pub source_project: Option<String>,
    /// `discard`: remove the change set's slot (the creator rejected it).
    #[serde(default)]
    pub action: Option<String>,
}

/// The answer of `POST /v1/stage`.
#[derive(Debug, Clone)]
pub struct StageAnswer {
    /// HTTP status (202 for a job, 200 for a discard).
    pub status: StatusCode,
    /// Body.
    pub body: Value,
}

/// The stage runner.
pub struct StageRunner {
    cfg: StageConfig,
    ledger: Arc<Ledger>,
    hub: EventHub,
    store: ArtifactStore,
    state_dir: PathBuf,
    root: PathBuf,
    repo: Option<PathBuf>,
}

impl std::fmt::Debug for StageRunner {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("StageRunner")
            .field("cfg", &self.cfg)
            .field("root", &self.root)
            .field("repo", &self.repo)
            .finish_non_exhaustive()
    }
}

impl StageRunner {
    /// A runner working under `state_dir/stage` (legacy shell) and the slot root (lane).
    pub fn new(
        cfg: StageConfig,
        ledger: Arc<Ledger>,
        hub: EventHub,
        store: ArtifactStore,
        state_dir: &Path,
    ) -> Arc<StageRunner> {
        let repo = std::env::var_os("GAMECORE_STAGE_REPO")
            .filter(|v| !v.is_empty())
            .map(PathBuf::from)
            .or_else(|| {
                cfg.command
                    .as_ref()
                    .and_then(|c| c.parent()?.parent()?.parent().map(Path::to_path_buf))
                    .filter(|r| r.join("studio/stage/make-slot.py").is_file())
            })
            .or_else(pipeline::discover_repo);
        StageRunner::with_paths(
            cfg,
            ledger,
            hub,
            store,
            state_dir,
            slot::default_root(),
            repo,
        )
    }

    /// A runner with an explicit slot root and repository checkout (tests, tools).
    pub fn with_paths(
        cfg: StageConfig,
        ledger: Arc<Ledger>,
        hub: EventHub,
        store: ArtifactStore,
        state_dir: &Path,
        root: PathBuf,
        repo: Option<PathBuf>,
    ) -> Arc<StageRunner> {
        Arc::new(StageRunner {
            cfg,
            ledger,
            hub,
            store,
            state_dir: state_dir.to_path_buf(),
            root,
            repo,
        })
    }

    /// Probe the configured lane at startup; unavailable confinement remains a refusal,
    /// while ordinary companion routes continue serving.
    pub fn probe_startup(self: &Arc<Self>) {
        if self.cfg.projects.is_empty() {
            return;
        }
        let me = self.clone();
        tokio::task::spawn_blocking(move || {
            let Some(repo) = &me.repo else { return };
            let mut sandbox = sandbox::Sandbox::defaults(
                &me.root.join(".startup-probe"),
                &me.root.join("_warm/probe"),
                repo,
            );
            sandbox.mode = me.cfg.confinement;
            sandbox.image = me.cfg.docker_image.clone();
            if let Err(error) = sandbox.probe() {
                tracing::warn!(error = %redact(&error), "stage_failed{{sandbox_unavailable}} at startup");
            }
        });
    }

    /// Mark jobs a previous process left unfinished as failed.
    pub fn settle_interrupted(&self) {
        if let Ok(ids) = self.ledger.unfinished_stages() {
            for id in ids {
                if let Some((job, repo)) = self.ledger.stage(&id).ok().zip(self.repo.as_ref()) {
                    let dir = job
                        .slot
                        .as_ref()
                        .filter(|slot| slot::valid_slot_id(slot))
                        .map(|slot| self.owner_root(&job.change_set_id).join(slot));
                    if let Some(dir) = dir {
                        sandbox::Sandbox::defaults(&dir, &self.root.join("_warm"), repo)
                            .stop_container();
                    }
                }
                let v = json!({"code": STAGE_FAILED, "message": "interrupted by a companion restart",
                               "hint": "stage the package again"});
                let _ = self.ledger.update_stage(&id, "failed", None, Some(&v));
            }
        }
    }

    /// Typed authenticated stage request. Source paths and compiler overrides are never
    /// accepted over HTTP; the operator registers project mappings in stage.projects.
    pub fn request_owned(self: &Arc<Self>, body: Value, owner: &str) -> ApiResult<StageAnswer> {
        let req: StageRunRequest = serde_json::from_value(body.clone())
            .map_err(|e| ApiError::bad_request(e.to_string()))?;
        let identity: Value = serde_json::from_str(owner)
            .map_err(|_| ApiError::bad_request("invalid project owner"))?;
        let project = identity[1]
            .as_str()
            .ok_or_else(|| ApiError::bad_request("project identity required"))?;
        if req.project_id.as_deref() != Some(project) || req.source_project.is_some() {
            return Err(ApiError::bad_request(
                "projectId must match X-GameCore-Project; sourceProject paths are not accepted",
            ));
        }
        let request = self.ledger.request(&req.change_set_id)?;
        if request.app != owner {
            return Err(ApiError::not_found("no candidate"));
        }
        if !matches!(req.action.as_deref(), None | Some("stage" | "discard")) {
            return Err(ApiError::bad_request("action is stage or discard"));
        }
        if req.action.as_deref() == Some("discard") {
            return self.discard(&req);
        }
        let path =
            self.cfg.projects.get(project).ok_or_else(|| {
                ApiError::stage_failed("project is not registered in stage.projects")
            })?;
        let catalog = req
            .catalog_revision
            .as_deref()
            .and_then(normalize_sha256)
            .ok_or_else(|| ApiError::bad_request("catalogRevision required"))?;
        if request.body["toolCatalogRevision"]
            .as_str()
            .and_then(normalize_sha256)
            != Some(catalog)
        {
            return Err(ApiError::stale_context(
                "catalog revision does not match the candidate",
            ));
        }
        let revision = std::process::Command::new("/usr/bin/git")
            .args(["-C"])
            .arg(path)
            .args(["rev-parse", "HEAD"])
            .output()
            .map_err(|e| ApiError::stage_failed(e.to_string()))?;
        let revision = String::from_utf8_lossy(&revision.stdout).trim().to_string();
        if req.source_revision.as_deref() != Some(revision.as_str()) || revision.len() != 40 {
            return Err(ApiError::stale_context(
                "sourceRevision does not match the registered source checkout",
            ));
        }
        let mut local = req;
        local.source_project = Some(path.display().to_string());
        self.submit_change_set(&local)
    }

    /// Operator/test entry point. The legacy packageRef extractor has been retired.
    pub fn request(self: &Arc<Self>, body: Value) -> ApiResult<StageAnswer> {
        let req: StageRunRequest = serde_json::from_value(body)
            .map_err(|e| ApiError::bad_request(format!("the body does not fit: {e}")))?;
        match req.action.as_deref() {
            None | Some("stage") => self.submit_change_set(&req),
            Some("discard") => self.discard(&req),
            Some(_) => Err(ApiError::bad_request("action is stage or discard")),
        }
    }

    /// Retrieve the issued record only. Partial and failed jobs have no attestation.
    pub fn signed_verdict(&self, job: &str) -> ApiResult<Value> {
        let row = self.ledger.stage(job)?;
        let record = row
            .verdict
            .ok_or_else(|| ApiError::not_found("no issued verdict"))?;
        let steps = record["steps"].as_array();
        let mandatory = steps.is_some_and(|steps| {
            steps.len() == verdict::STEP_IDS.len()
                && verdict::STEP_IDS.iter().all(|id| {
                    steps
                        .iter()
                        .any(|s| s["id"] == *id && s["status"] == "pass")
                })
        });
        if record["signature"].is_string()
            && record["pass"] == true
            && record["partial"] != true
            && mandatory
            && record["jobId"] == job
            && record["forbiddenHits"]
                .as_array()
                .is_some_and(Vec::is_empty)
            && signing::verify(&self.state_dir, &record).map_err(ApiError::internal)?
        {
            Ok(record)
        } else {
            Err(ApiError::not_found("no issued passing verdict"))
        }
    }

    /// Verify against both the installation key and the exact issued ledger record.
    pub fn verify_verdict(&self, job: &str, record: &Value) -> ApiResult<bool> {
        let issued = self.signed_verdict(job)?;
        Ok(&issued == record
            && signing::verify(&self.state_dir, record).map_err(ApiError::internal)?)
    }

    fn repo(&self) -> ApiResult<PathBuf> {
        self.repo.clone().ok_or_else(|| {
            ApiError::stage_failed("no game_core checkout with studio/stage/make-slot.py was found")
                .with_hint("set GAMECORE_STAGE_REPO to the repository the staging lane runs from")
        })
    }

    fn owner_root(&self, change_set: &str) -> PathBuf {
        self.ledger
            .request(change_set)
            .ok()
            .filter(|row| serde_json::from_str::<Value>(&row.app).is_ok_and(|v| v.is_array()))
            .map(|row| self.root.join(crate::util::sha256_hex(row.app.as_bytes())))
            .unwrap_or_else(|| self.root.clone())
    }

    fn discard(&self, req: &StageRunRequest) -> ApiResult<StageAnswer> {
        if !valid_change_set_id(&req.change_set_id) {
            return Err(ApiError::bad_request("changeSetId is not a change-set id"));
        }
        let holding = slot::slots(&self.owner_root(&req.change_set_id))
            .into_iter()
            .find(|d| slot::slot_change_set(d).as_deref() == Some(req.change_set_id.as_str()));
        let Some(dir) = holding else {
            return Ok(StageAnswer {
                status: StatusCode::OK,
                body: json!({"changeSetId": req.change_set_id, "discarded": false}),
            });
        };
        let _lock =
            slot::SlotLock::acquire(&dir, slot::MAX_SLOT_AGE).map_err(ApiError::ledger_conflict)?;
        let name = dir
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        slot::remove_slot(&dir)
            .map_err(|e| ApiError::internal(format!("cannot remove slot {name}: {e}")))?;
        tracing::info!(slot = %name, change_set = %req.change_set_id, "staging slot discarded");
        Ok(StageAnswer {
            status: StatusCode::OK,
            body: json!({"changeSetId": req.change_set_id, "slot": name, "discarded": true}),
        })
    }

    /// The staging lane: `POST /v1/stage {changeSetId, slot?, steps?, sourceProject?}`.
    pub fn submit_change_set(self: &Arc<Self>, req: &StageRunRequest) -> ApiResult<StageAnswer> {
        if !valid_change_set_id(&req.change_set_id) {
            return Err(ApiError::bad_request("changeSetId is not a change-set id"));
        }
        let steps = match &req.steps {
            None => None,
            Some(list) => {
                if let Some(bad) = list
                    .iter()
                    .find(|s| !verdict::STEP_IDS.contains(&s.as_str()))
                {
                    return Err(ApiError::bad_request(format!(
                        "unknown step {bad:?}; steps are {}",
                        verdict::STEP_IDS.join(", ")
                    )));
                }
                Some(list.iter().cloned().collect::<BTreeSet<String>>())
            }
        };
        let repo = self.repo()?;
        let candidate = self
            .ledger
            .candidate(&req.change_set_id)
            .map_err(|e| match e {
                LedgerError::NotFound(_) => ApiError::not_found(format!(
                    "no candidate {}; the staging lane stages validated candidates",
                    req.change_set_id
                )),
                other => other.into(),
            })?;
        let package_ref = proposed_package(&candidate.change_set).ok_or_else(|| {
            ApiError::bad_request(
                "the change set carries no mechanism.propose with a package artifact",
            )
        })?;
        let root = self.owner_root(&req.change_set_id);
        let collected = slot::gc(&root, slot::MAX_SLOT_AGE);
        if !collected.is_empty() {
            tracing::info!(slots = ?collected, "collected staging slots older than seven days");
        }
        let slot_id = slot::resolve_slot(&root, &req.change_set_id, req.slot.as_deref())
            .map_err(ApiError::ledger_conflict)?;
        if slot::is_locked(&root.join(&slot_id)) {
            return Err(ApiError::ledger_conflict(format!(
                "slot {slot_id} is being staged; one stage per slot"
            )));
        }
        let mut opts =
            pipeline::StageOptions::from_env(&repo, &slot_id, pipeline::SlotSource::Existing);
        opts.root = root;
        opts.expected_source_revision = req.source_revision.clone();
        opts.sandbox.mode = self.cfg.confinement;
        opts.sandbox.image = self.cfg.docker_image.clone();
        // Service lane has fixed tool paths. CLI overrides remain operator-only.
        opts.tools.python = PathBuf::from("/usr/bin/python3");
        opts.tools.dotnet = std::env::var_os("HOME")
            .map(PathBuf::from)
            .map(|p| p.join(".dotnet/dotnet"))
            .filter(|p| p.is_file())
            .unwrap_or_else(|| PathBuf::from("/usr/bin/dotnet"));
        opts.budget = Duration::from_secs(360);
        opts.steps = steps;
        if let Some(p) = &req.source_project {
            opts.source_project = PathBuf::from(p);
        }
        if !opts.source_project.join("ProjectSettings").is_dir() {
            return Err(ApiError::bad_request(format!(
                "the source project {} is not a Unity project",
                opts.source_project.display()
            )));
        }
        let now = now_ms();
        let job = StageJobView {
            job_id: new_id("stg"),
            change_set_id: req.change_set_id.clone(),
            package_ref,
            state: "queued".into(),
            slot: Some(slot_id),
            verdict: None,
            created_at: now,
            updated_at: now,
        };
        self.ledger.insert_stage(&job)?;
        self.emit(&job);
        let me = self.clone();
        let id = job.job_id.clone();
        let change_set = candidate.change_set;
        let artifacts = candidate.artifacts;
        tokio::task::spawn_blocking(move || me.run_lane(&id, opts, &change_set, &artifacts));
        Ok(StageAnswer {
            status: StatusCode::ACCEPTED,
            body: serde_json::to_value(job).unwrap_or(Value::Null),
        })
    }

    /// Write the candidate (change set + stored artifacts) where make-slot.py reads it.
    fn materialise(&self, dir: &Path, change_set: &Value, artifacts: &Value) -> Result<(), String> {
        std::fs::create_dir_all(dir.join("artifacts")).map_err(|e| e.to_string())?;
        let bytes = serde_json::to_vec_pretty(change_set).map_err(|e| e.to_string())?;
        std::fs::write(dir.join("change-set.json"), bytes).map_err(|e| e.to_string())?;
        for a in artifacts.as_array().into_iter().flatten() {
            let Some(sha) = a["sha256"].as_str().and_then(normalize_sha256) else {
                continue;
            };
            let data = self
                .store
                .get(&sha)
                .map_err(|e| e.to_string())?
                .ok_or_else(|| format!("artifact {sha} is not in the content store"))?;
            std::fs::write(dir.join("artifacts").join(&sha), data).map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    fn run_lane(
        &self,
        id: &str,
        mut opts: pipeline::StageOptions,
        change_set: &Value,
        artifacts: &Value,
    ) {
        let incoming = self.root.join(".incoming").join(id);
        if let Ok(job) = self.ledger.update_stage(id, "running", None, None) {
            self.emit(&job);
        }
        let outcome = self
            .materialise(&incoming, change_set, artifacts)
            .and_then(|()| {
                opts.source = pipeline::SlotSource::Candidate(incoming.clone());
                pipeline::run_stage(&opts)
            });
        let _ = std::fs::remove_dir_all(&incoming);
        let (state, value) = match outcome {
            Ok(v) => {
                let mut record = v.to_value();
                if let Ok(request) = self.ledger.request(&v.change_set_id) {
                    let owner: Value = serde_json::from_str(&request.app).unwrap_or(Value::Null);
                    record["jobId"] = json!(id);
                    record["projectId"] = owner[1].clone();
                    record["app"] = owner[0].clone();
                    record["sourceRevision"] = json!(v.runner.source_commit);
                    record["catalogRevision"] = request.body["toolCatalogRevision"].clone();
                    record["packageDigest"] = json!(
                        v.artifacts
                            .iter()
                            .find(|a| a.role == "package")
                            .map(|a| &a.sha256)
                    );
                    record["proposalDigest"] = json!(
                        v.artifacts
                            .iter()
                            .find(|a| a.role == "proposal")
                            .map(|a| &a.sha256)
                    );
                }
                let bytes = v.bytes();
                let stored = self.store.put(&bytes, None).map(|(sha, _)| sha);
                let mut value = record;
                if let Some(o) = value.as_object_mut() {
                    match stored {
                        Ok(sha) => {
                            o.insert("verdictRef".into(), json!(sha));
                        }
                        Err(e) => {
                            tracing::error!(job = id, error = %e, "cannot store the stage verdict");
                        }
                    }
                    if let Some(reason) = &v.failure {
                        o.insert("code".into(), json!(STAGE_FAILED));
                        o.insert("reason".into(), json!(reason));
                        o.insert(
                            "message".into(),
                            json!(format!("the stage failed: {reason}")),
                        );
                        o.insert(
                            "hint".into(),
                            json!("B-STAGE is 6 min; a cold slot imports the whole project once (GAMECORE_STAGE_BUDGET_S)"),
                        );
                    }
                }
                if v.pass {
                    let required = [
                        "jobId",
                        "projectId",
                        "app",
                        "sourceRevision",
                        "catalogRevision",
                        "packageDigest",
                        "proposalDigest",
                    ];
                    if required
                        .iter()
                        .any(|key| !value[*key].as_str().is_some_and(|v| !v.is_empty()))
                    {
                        self.finish(
                            id,
                            "failed",
                            &json!({"code": STAGE_FAILED,"reason":"missing_attestation_binding"}),
                        );
                        return;
                    }
                    value = match signing::sign(&self.state_dir, value) {
                        Ok(record) => record,
                        Err(error) => {
                            self.finish(id, "failed", &json!({"code":STAGE_FAILED,"reason":"signing_unavailable","message":redact(&error)}));
                            return;
                        }
                    };
                }
                if let Some((request, reference)) = self
                    .ledger
                    .request(&v.change_set_id)
                    .ok()
                    .zip(value["verdictRef"].as_str())
                {
                    let _ = self.ledger.grant("artifact", reference, &request.app);
                }
                tracing::info!(job = id, pass = v.pass, slot = %v.slot, ms = v.duration_ms, "stage verdict");
                (
                    if v.failure.is_some() {
                        "failed"
                    } else {
                        "done"
                    },
                    value,
                )
            }
            Err(message) => (
                "failed",
                json!({"code": STAGE_FAILED, "reason": if message.contains("sandbox_unavailable") { "sandbox_unavailable" } else { "stage_error" }, "message": redact(&message),
                       "hint": "see the slot's out/logs and the companion log"}),
            ),
        };
        self.finish(id, state, &value);
    }

    fn emit(&self, job: &StageJobView) {
        let _ = self.hub.emit(
            "stage",
            Some(&job.change_set_id),
            &serde_json::to_value(job).unwrap_or(Value::Null),
        );
    }

    fn finish(&self, id: &str, state: &str, verdict: &Value) {
        match self.ledger.update_stage(id, state, None, Some(verdict)) {
            Ok(job) => self.emit(&job),
            Err(e) => tracing::error!(job = id, error = %e, "cannot record the stage verdict"),
        }
    }
}

/// The package artifact digest of a change set's `mechanism.propose` (or `mechanism.admit`):
/// `args.package` as `"sha256:<hex>"` or `{"artifact": "sha256:<hex>"}`.
pub fn proposed_package(change_set: &Value) -> Option<String> {
    change_set["operations"]
        .as_array()?
        .iter()
        .filter(|op| {
            matches!(
                op["tool"].as_str(),
                Some("mechanism.propose" | "mechanism.admit")
            )
        })
        .find_map(|op| {
            let p = &op["args"]["package"];
            p.as_str()
                .or_else(|| p["artifact"].as_str())
                .and_then(normalize_sha256)
        })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn verdicts_and_env() {
        assert_eq!(
            parse_verdict("compiling\n{\"ok\":true,\"tests\":{\"passed\":3}}\n"),
            Some(json!({"ok": true, "tests": {"passed": 3}}))
        );
        assert_eq!(
            parse_verdict("{\n \"ok\": false\n}"),
            Some(json!({"ok": false}))
        );
        assert_eq!(parse_verdict("nothing"), None);
        assert!(stage_env().iter().all(|(k, _)| !env::secret_like(k)));
    }

    #[test]
    fn the_proposed_package_is_found_in_either_argument_shape() {
        let sha = "a".repeat(64);
        let cs = json!({"operations": [
            {"opId": "op_1", "tool": "asset.create", "args": {"package": "sha256:ignored"}},
            {"opId": "op_2", "tool": "mechanism.propose", "args": {"package": {"artifact": format!("sha256:{sha}")}}}
        ]});
        assert_eq!(proposed_package(&cs), Some(sha.clone()));
        let cs = json!({"operations": [{"tool": "mechanism.admit", "args": {"package": format!("sha256:{}", sha.to_uppercase())}}]});
        assert_eq!(proposed_package(&cs), Some(sha));
        assert_eq!(proposed_package(&json!({"operations": []})), None);
        assert_eq!(proposed_package(&json!({})), None);
    }

    #[test]
    fn lane_requests_are_strict() {
        let ok: StageRunRequest = serde_json::from_value(json!({
            "changeSetId": "cs_01JAPP0000000000000000PXAT", "slot": "s1", "steps": ["scan"]
        }))
        .unwrap();
        assert_eq!(ok.slot.as_deref(), Some("s1"));
        assert!(
            serde_json::from_value::<StageRunRequest>(json!({
                "changeSetId": "cs_01JAPP0000000000000000PXAT", "unexpected": 1
            }))
            .is_err()
        );
    }
}
