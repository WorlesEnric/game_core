//! The staging shell (04 §6). P2.4 owns the staging lane itself (`studio/stage/stage.sh`, slot
//! creation, the verdict's content); the companion's part is this runner:
//!
//! `POST /v1/stage {changeSetId, packageRef}` → a job: the package artifact (a `.tar.gz` or
//! `.tar` from the content store) is extracted into `ETOS_STATE_DIR/stage/<job>/package`, a
//! free slot is taken, and `<command> <slot> <package-dir>` runs with a watchdog and a minimal
//! environment (no provider keys: the companion inherits etosd's environment, the stage
//! script does not). The verdict is the last JSON object the command prints on stdout
//! (`{ok, compile:{errors[]}, tests:{passed,failed,names[]}, forbidden:[...], durationMs}`);
//! none, a timeout or a missing command is `stage_failed` with a hint.

use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde_json::{Value, json};
use tokio::sync::Notify;

use crate::config::StageConfig;
use crate::error::{ApiError, ApiResult, STAGE_FAILED};
use crate::events::EventHub;
use crate::ledger::Ledger;
use crate::model::{StageJobView, StageRequest};
use crate::redact::redact;
use crate::store::ArtifactStore;
use crate::util::{new_id, normalize_sha256, now_ms, valid_change_set_id};

/// Environment variables passed to the stage command (plus `GAMECORE_*`, `UNITY_*`,
/// `DOTNET_*` names that do not look like secrets).
const ENV_ALLOW: &[&str] = &[
    "PATH",
    "HOME",
    "USER",
    "LOGNAME",
    "LANG",
    "LC_ALL",
    "TMPDIR",
    "DISPLAY",
    "XAUTHORITY",
    "SHELL",
    "XDG_RUNTIME_DIR",
];

fn secret_like(name: &str) -> bool {
    let n = name.to_ascii_uppercase();
    ["KEY", "TOKEN", "SECRET", "PASSWORD", "CREDENTIAL"]
        .iter()
        .any(|s| n.contains(s))
}

/// The environment the stage command gets.
pub fn stage_env() -> Vec<(String, String)> {
    std::env::vars()
        .filter(|(k, _)| {
            (ENV_ALLOW.contains(&k.as_str())
                || k.starts_with("GAMECORE_")
                || k.starts_with("UNITY_")
                || k.starts_with("DOTNET_"))
                && !secret_like(k)
        })
        .collect()
}

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

/// The stage runner.
pub struct StageRunner {
    cfg: StageConfig,
    ledger: Arc<Ledger>,
    hub: EventHub,
    store: ArtifactStore,
    work: PathBuf,
    free: Mutex<Vec<String>>,
    released: Notify,
}

impl std::fmt::Debug for StageRunner {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("StageRunner")
            .field("cfg", &self.cfg)
            .finish_non_exhaustive()
    }
}

impl StageRunner {
    /// A runner working under `state_dir/stage`.
    pub fn new(
        cfg: StageConfig,
        ledger: Arc<Ledger>,
        hub: EventHub,
        store: ArtifactStore,
        state_dir: &Path,
    ) -> Arc<StageRunner> {
        let free = cfg.slots.iter().rev().cloned().collect();
        Arc::new(StageRunner {
            cfg,
            ledger,
            hub,
            store,
            work: state_dir.join("stage"),
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

    /// `POST /v1/stage`.
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
        tokio::spawn(async move { me.run(id, command, package).await });
        Ok(job)
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

    async fn run(self: Arc<Self>, id: String, command: PathBuf, package: PathBuf) {
        let slot = self.take_slot().await;
        match self.ledger.update_stage(&id, "running", Some(&slot), None) {
            Ok(job) => self.emit(&job),
            Err(e) => tracing::error!(job = %id, error = %e, "cannot record the stage start"),
        }
        let (state, verdict) = self.execute(&id, &slot, &command, &package).await;
        self.give_slot(slot);
        self.finish(&id, state, &verdict);
    }

    async fn execute(
        &self,
        id: &str,
        slot: &str,
        command: &Path,
        package: &Path,
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
        assert!(secret_like("ECHO_API_KEY"));
        assert!(secret_like("GAMECORE_TOKEN"));
        assert!(!secret_like("GAMECORE_SLOT_ROOT"));
        assert!(stage_env().iter().all(|(k, _)| !secret_like(k)));
    }
}
