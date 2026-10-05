//! The staging lane (P2.4; 03 §8, 04 §6): generated code is staged and validated in an
//! isolated Unity project + dotnet workspace (a *slot*) before it can affect the live editor.
//!
//! `POST /v1/stage` takes two shapes:
//!
//! * **`{changeSetId, slot?, steps?, sourceProject?}`** — the staging lane. The candidate
//!   (validated change set + artifacts, from the ledger and the content store) is written to a
//!   slot under `~/.cache/gamecore-studio/stage/<slot>/` (`GAMECORE_STAGE_ROOT`) and the
//!   pipeline ([`pipeline`]) runs: forbidden-content scan, repository checkers, dotnet,
//!   batchmode Unity EditMode, PlayMode smoke, determinism, B-STAGE budget. The answer is a
//!   job; the job's `verdict` is the [`verdict::StageVerdict`] plus its `verdictRef` (the
//!   SHA-256 of the verdict's canonical JSON, also stored as an artifact). One stage per slot,
//!   one slot per change set; slots older than seven days are collected, and
//!   `{changeSetId, action: "discard"}` (the creator rejected the change set) removes its slot.
//! * **`{changeSetId, packageRef}`** — the legacy shell (P0.5): the package archive is
//!   extracted and `<command> <slot> <package-dir> <changeSetId>` runs (`studio/stage/stage.sh`
//!   wraps the lane's CLI); its last JSON stdout line is the verdict.
//!
//! The CLI is `gamecore-studio stage run|gc|discard|scan` ([`cli`]). Every child process gets
//! `env_clear()` plus the allowlist of [`env`] (no provider keys); every log is redacted.

pub mod cli;
pub mod env;
pub mod pipeline;
pub mod scan;
pub mod slot;
pub mod verdict;

use std::collections::BTreeSet;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use axum::http::StatusCode;
use serde::Deserialize;
use serde_json::{Value, json};
use tokio::sync::Notify;

pub use env::stage_env;

use crate::config::StageConfig;
use crate::error::{ApiError, ApiResult, STAGE_FAILED};
use crate::events::EventHub;
use crate::ledger::{Ledger, LedgerError};
use crate::model::{StageJobView, StageRequest};
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
    work: PathBuf,
    root: PathBuf,
    repo: Option<PathBuf>,
    free: Mutex<Vec<String>>,
    released: Notify,
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
        let free = cfg.slots.iter().rev().cloned().collect();
        Arc::new(StageRunner {
            cfg,
            ledger,
            hub,
            store,
            work: state_dir.join("stage"),
            root,
            repo,
            free: Mutex::new(free),
            released: Notify::new(),
        })
    }

    /// Mark jobs a previous process left unfinished as failed.
    pub fn settle_interrupted(&self) {
        if let Ok(ids) = self.ledger.unfinished_stages() {
            for id in ids {
                let v = json!({"code": STAGE_FAILED, "message": "interrupted by a companion restart",
                               "hint": "stage the package again"});
                let _ = self.ledger.update_stage(&id, "failed", None, Some(&v));
            }
        }
    }

    fn command(&self) -> ApiResult<PathBuf> {
        match &self.cfg.command {
            Some(p) if p.is_file() => Ok(p.clone()),
            Some(p) => Err(ApiError::stage_failed(format!(
                "the staging command {} does not exist",
                p.display()
            ))
            .with_hint("P2.4 provides studio/stage/stage.sh; set [stage] command in <state>/config.toml or GAMECORE_STUDIO_STAGE_COMMAND")),
            None => Err(ApiError::stage_failed("no staging command is configured").with_hint(
                "P2.4 provides studio/stage/stage.sh; set [stage] command in <state>/config.toml or GAMECORE_STUDIO_STAGE_COMMAND",
            )),
        }
    }

    /// `POST /v1/stage`, either shape.
    pub fn request(self: &Arc<Self>, body: Value) -> ApiResult<StageAnswer> {
        if body.get("packageRef").is_some() {
            let req: StageRequest = serde_json::from_value(body)
                .map_err(|e| ApiError::bad_request(format!("the body does not fit: {e}")))?;
            let job = self.submit(&req)?;
            return Ok(StageAnswer {
                status: StatusCode::ACCEPTED,
                body: serde_json::to_value(job).unwrap_or(Value::Null),
            });
        }
        let req: StageRunRequest = serde_json::from_value(body)
            .map_err(|e| ApiError::bad_request(format!("the body does not fit: {e}")))?;
        match req.action.as_deref() {
            None | Some("stage") => self.submit_change_set(&req),
            Some("discard") => self.discard(&req),
            Some(other) => Err(ApiError::bad_request(format!(
                "action {other:?} is not stage or discard"
            ))),
        }
    }

    /// The legacy shell: `POST /v1/stage {changeSetId, packageRef}`.
    pub fn submit(self: &Arc<Self>, req: &StageRequest) -> ApiResult<StageJobView> {
        if !valid_change_set_id(&req.change_set_id) {
            return Err(ApiError::bad_request("changeSetId is not a change-set id"));
        }
        let sha = normalize_sha256(&req.package_ref)
            .ok_or_else(|| ApiError::bad_request("packageRef is a sha256 digest"))?;
        let command = self.command()?;
        let package = self
            .store
            .path_of(&sha)
            .map_err(|e| ApiError::bad_request(e.to_string()))?;
        if !package.is_file() {
            return Err(ApiError::not_found(format!("no stored artifact {sha}")));
        }
        let now = now_ms();
        let job = StageJobView {
            job_id: new_id("stg"),
            change_set_id: req.change_set_id.clone(),
            package_ref: sha,
            state: "queued".into(),
            slot: None,
            verdict: None,
            created_at: now,
            updated_at: now,
        };
        self.ledger.insert_stage(&job)?;
        self.emit(&job);
        let me = self.clone();
        let id = job.job_id.clone();
        let cs = job.change_set_id.clone();
        tokio::spawn(async move { me.run(id, command, package, cs).await });
        Ok(job)
    }

    fn repo(&self) -> ApiResult<PathBuf> {
        self.repo.clone().ok_or_else(|| {
            ApiError::stage_failed("no game_core checkout with studio/stage/make-slot.py was found")
                .with_hint("set GAMECORE_STAGE_REPO to the repository the staging lane runs from")
        })
    }

    fn discard(&self, req: &StageRunRequest) -> ApiResult<StageAnswer> {
        if !valid_change_set_id(&req.change_set_id) {
            return Err(ApiError::bad_request("changeSetId is not a change-set id"));
        }
        let holding = slot::slots(&self.root)
            .into_iter()
            .find(|d| slot::slot_change_set(d).as_deref() == Some(req.change_set_id.as_str()));
        let Some(dir) = holding else {
            return Ok(StageAnswer {
                status: StatusCode::OK,
                body: json!({"changeSetId": req.change_set_id, "discarded": false}),
            });
        };
        if slot::is_locked(&dir) {
            return Err(ApiError::ledger_conflict(
                "the change set is being staged; discard it when the stage ends",
            ));
        }
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
        let collected = slot::gc(&self.root, slot::MAX_SLOT_AGE);
        if !collected.is_empty() {
            tracing::info!(slots = ?collected, "collected staging slots older than seven days");
        }
        let slot_id = slot::resolve_slot(&self.root, &req.change_set_id, req.slot.as_deref())
            .map_err(ApiError::ledger_conflict)?;
        if slot::is_locked(&self.root.join(&slot_id)) {
            return Err(ApiError::ledger_conflict(format!(
                "slot {slot_id} is being staged; one stage per slot"
            )));
        }
        let mut opts =
            pipeline::StageOptions::from_env(&repo, &slot_id, pipeline::SlotSource::Existing);
        opts.root = self.root.clone();
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
                let bytes = v.bytes();
                let stored = self.store.put(&bytes, None).map(|(sha, _)| sha);
                let mut value = v.to_value();
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
                json!({"code": STAGE_FAILED, "message": redact(&message),
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

    async fn take_slot(&self) -> String {
        loop {
            let notified = self.released.notified();
            if let Ok(mut f) = self.free.lock()
                && let Some(s) = f.pop()
            {
                return s;
            }
            notified.await;
        }
    }

    fn give_slot(&self, slot: String) {
        if let Ok(mut f) = self.free.lock() {
            f.push(slot);
        }
        self.released.notify_one();
    }

    fn finish(&self, id: &str, state: &str, verdict: &Value) {
        match self.ledger.update_stage(id, state, None, Some(verdict)) {
            Ok(job) => self.emit(&job),
            Err(e) => tracing::error!(job = id, error = %e, "cannot record the stage verdict"),
        }
    }

    async fn run(self: Arc<Self>, id: String, command: PathBuf, package: PathBuf, cs: String) {
        let slot = self.take_slot().await;
        match self.ledger.update_stage(&id, "running", Some(&slot), None) {
            Ok(job) => self.emit(&job),
            Err(e) => tracing::error!(job = %id, error = %e, "cannot record the stage start"),
        }
        let (state, verdict) = self.execute(&id, &slot, &command, &package, &cs).await;
        self.give_slot(slot);
        self.finish(&id, state, &verdict);
    }

    async fn execute(
        &self,
        id: &str,
        slot: &str,
        command: &Path,
        package: &Path,
        change_set_id: &str,
    ) -> (&'static str, Value) {
        let fail = |message: String, hint: &str, extra: Value| {
            let mut v = json!({"code": STAGE_FAILED, "message": redact(&message), "hint": hint});
            if let (Some(o), Value::Object(e)) = (v.as_object_mut(), extra) {
                o.extend(e);
            }
            ("failed", v)
        };
        let dir = self.work.join(id).join("package");
        if let Err(e) = std::fs::create_dir_all(&dir) {
            return fail(
                format!("cannot create {}: {e}", dir.display()),
                "check the state directory",
                json!({}),
            );
        }
        let head = std::fs::read(package)
            .map(|b| b.into_iter().take(300).collect::<Vec<u8>>())
            .unwrap_or_default();
        let flags = if head.starts_with(&[0x1f, 0x8b]) {
            "-xzf"
        } else if head.len() >= 262 && &head[257..262] == b"ustar" {
            "-xf"
        } else {
            return fail(
                "the package artifact is not a .tar.gz or .tar".into(),
                "the mechanic writes /outputs/package.tgz (tar czf) and lists it in the change set",
                json!({}),
            );
        };
        let untar = tokio::process::Command::new("tar")
            .arg(flags)
            .arg(package)
            .arg("-C")
            .arg(&dir)
            .env_clear()
            .envs(stage_env())
            .output()
            .await;
        match untar {
            Ok(o) if o.status.success() => {}
            Ok(o) => {
                return fail(
                    format!("tar failed: {}", String::from_utf8_lossy(&o.stderr)),
                    "the package archive is damaged",
                    json!({}),
                );
            }
            Err(e) => {
                return fail(
                    format!("cannot run tar: {e}"),
                    "install tar on the host",
                    json!({}),
                );
            }
        }
        let started = std::time::Instant::now();
        let child = tokio::process::Command::new(command)
            .arg(slot)
            .arg(&dir)
            .arg(change_set_id)
            .env_clear()
            .envs(stage_env())
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true)
            .spawn();
        let child = match child {
            Ok(c) => c,
            Err(e) => {
                return fail(
                    format!("cannot start {}: {e}", command.display()),
                    "check that the command is executable",
                    json!({}),
                );
            }
        };
        tracing::info!(job = id, slot, command = %command.display(), "staging");
        let out = tokio::time::timeout(
            Duration::from_secs(self.cfg.timeout_s),
            child.wait_with_output(),
        )
        .await;
        let out = match out {
            Ok(Ok(o)) => o,
            Ok(Err(e)) => {
                return fail(
                    format!("the staging command failed: {e}"),
                    "see the companion log",
                    json!({}),
                );
            }
            Err(_) => {
                return fail(
                    format!(
                        "the staging command did not finish within {} s",
                        self.cfg.timeout_s
                    ),
                    "the stage script has its own 10-minute watchdog; check for an Editor hang",
                    json!({"durationMs": started.elapsed().as_millis() as u64}),
                );
            }
        };
        let stdout = String::from_utf8_lossy(&out.stdout);
        let stderr = String::from_utf8_lossy(&out.stderr);
        let tail: String = {
            let chars: Vec<char> = stderr.chars().collect();
            chars[chars.len().saturating_sub(2000)..].iter().collect()
        };
        let code = out.status.code();
        match parse_verdict(&stdout) {
            Some(mut v) => {
                if let Some(o) = v.as_object_mut() {
                    o.entry("exitCode").or_insert(json!(code));
                    o.entry("durationMs")
                        .or_insert(json!(started.elapsed().as_millis() as u64));
                }
                ("done", v)
            }
            None => fail(
                format!("the staging command printed no JSON verdict (exit {code:?})"),
                "stage.sh must print the verdict object as its last stdout line",
                json!({"exitCode": code, "stderrTail": redact(&tail)}),
            ),
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
