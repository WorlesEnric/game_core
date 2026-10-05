//! Configuration: defaults, then the operator's optional `config.toml` in `ETOS_STATE_DIR`,
//! then a few `GAMECORE_STUDIO_*` environment overrides (settable through `[process] env` of
//! `agent.toml`). Nothing here holds a secret.
//!
//! ```toml
//! # $ETOS_STATE_DIR/config.toml (all keys optional)
//! allowed_apps = ["gamecore-unity"]
//! workers = ["gc-designer", "gc-mechanic"]
//! default_worker = "gc-designer"
//! reask_on_invalid = true
//! port = 7451                       # loopback listener; else the saved port, else ephemeral
//! schema = "/path/to/change-set.schema.json"   # else the vendored copy
//! follow_wait_ms = 20000
//! ops_max_cost_usd = 0.10           # default ceiling of /v1/ops/generate when a call has none
//! task_open_timeout_secs = 180      # POST /tasks launches the task inline; give it time
//! ops_timeout_secs = 300            # one media operation (generate.image can take minutes)
//! [stage]
//! command = "/home/me/wkspace/game_core/studio/stage/stage.sh"
//! slots = ["1", "2"]
//! timeout_s = 900
//! [voice]
//! provider = "studio-voice"
//! instructions = "Transcribe the user's speech."
//! max_sessions = 1
//! ```

use std::path::{Path, PathBuf};

use serde::Deserialize;

/// The Unity app allowed by default.
pub const DEFAULT_APP: &str = "gamecore-unity";
/// The agent's name by default (`ETOS_AGENT` wins).
pub const DEFAULT_AGENT: &str = "gamecore-studio";

/// The staging shell's settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StageConfig {
    /// OS execution boundary; default Docker, host is explicit degraded opt-in.
    pub confinement: crate::stage::sandbox::Confinement,
    /// Pre-provisioned, versioned sandbox image (no implicit pull).
    pub docker_image: String,
    /// Operator-owned map of project identities to local source paths.
    pub projects: std::collections::BTreeMap<String, PathBuf>,
    /// `stage.sh` (called as `<command> <slot> <package-dir>`); `None` when not found.
    pub command: Option<PathBuf>,
    /// Slot names handed to the command; one job per slot at a time.
    pub slots: Vec<String>,
    /// Watchdog for one job, in seconds (the script has its own 10-minute watchdog).
    pub timeout_s: u64,
}

/// The voice bridge's settings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VoiceConfig {
    /// The etos realtime provider (`[[realtime]] name` in `ops.toml`).
    pub provider: String,
    /// Instructions sent in the session configuration.
    pub instructions: String,
    /// Concurrent sessions (one per Studio instance).
    pub max_sessions: usize,
}

/// The companion's configuration.
#[derive(Debug, Clone, PartialEq)]
pub struct Config {
    /// The agent's name (its topics are `#agent/<agent>/...`, its binding is `<agent>`).
    pub agent: String,
    /// `ETOS_STATE_DIR`: ledger, artifacts, logger state, port.
    pub state_dir: PathBuf,
    /// Apps (`X-Etos-App`) allowed to call the API.
    pub allowed_apps: Vec<String>,
    /// Workers requests may name (the manifest's `[[worker]]`s).
    pub workers: Vec<String>,
    /// The worker of a request that names none.
    pub default_worker: String,
    /// Re-ask once (a new task) when a candidate is invalid.
    pub reask_on_invalid: bool,
    /// The loopback port; `None`: the saved port, else ephemeral.
    pub port: Option<u16>,
    /// An external change-set schema; `None`: the vendored copy.
    pub schema: Option<PathBuf>,
    /// Long-poll wait of the topic follower, in ms.
    pub follow_wait_ms: u64,
    /// How long `/v1/hello` reuses the provider status, in seconds.
    pub hello_cache_s: u64,
    /// Timeout of one `POST /tasks` call, in seconds. etos launches the task inside that
    /// request, so a short client timeout can drop it mid-launch (P0.1 DIAGNOSIS item 2).
    pub task_open_timeout_secs: u64,
    /// Timeout of one media operation (`POST /ops/generate.image`, `tts`, ...), in seconds:
    /// `/v1/ops/generate` holds the caller's request this long.
    pub ops_timeout_secs: u64,
    /// Coalescing window of index deltas, in ms (at most one batch per window).
    pub index_flush_ms: u64,
    /// Cap of the index slice packed for a worker, in bytes (truncated beyond).
    pub max_slice_bytes: usize,
    /// Default `max_cost_usd` of `/v1/ops/generate` for a call that names none; `None`: such
    /// a call is refused (`bad_request`).
    pub ops_max_cost_usd: Option<f64>,
    /// Staging.
    pub stage: StageConfig,
    /// Voice.
    pub voice: VoiceConfig,
}

#[derive(Debug, Default, Deserialize)]
#[serde(deny_unknown_fields)]
struct FileConfig {
    allowed_apps: Option<Vec<String>>,
    workers: Option<Vec<String>>,
    default_worker: Option<String>,
    reask_on_invalid: Option<bool>,
    port: Option<u16>,
    schema: Option<PathBuf>,
    follow_wait_ms: Option<u64>,
    hello_cache_s: Option<u64>,
    task_open_timeout_secs: Option<u64>,
    ops_timeout_secs: Option<u64>,
    index_flush_ms: Option<u64>,
    max_slice_bytes: Option<usize>,
    ops_max_cost_usd: Option<f64>,
    stage: Option<FileStage>,
    voice: Option<FileVoice>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(deny_unknown_fields)]
struct FileStage {
    confinement: Option<crate::stage::sandbox::Confinement>,
    docker_image: Option<String>,
    projects: Option<std::collections::BTreeMap<String, PathBuf>>,
    command: Option<PathBuf>,
    slots: Option<Vec<String>>,
    timeout_s: Option<u64>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(deny_unknown_fields)]
struct FileVoice {
    provider: Option<String>,
    instructions: Option<String>,
    max_sessions: Option<usize>,
}

/// Why the configuration could not be loaded.
#[derive(Debug, thiserror::Error)]
pub enum ConfigError {
    /// A required variable is missing.
    #[error("{0} is not set; the node sets it when it starts an installed agent")]
    Missing(&'static str),
    /// The file could not be read or parsed.
    #[error("{path}: {message}")]
    File {
        /// The file.
        path: String,
        /// What is wrong.
        message: String,
    },
}

impl Config {
    /// Defaults for a state directory.
    pub fn defaults(agent: &str, state_dir: &Path) -> Config {
        Config {
            agent: agent.to_string(),
            state_dir: state_dir.to_path_buf(),
            allowed_apps: vec![DEFAULT_APP.to_string()],
            workers: vec!["gc-designer".into(), "gc-mechanic".into()],
            default_worker: "gc-designer".into(),
            reask_on_invalid: true,
            port: None,
            schema: None,
            follow_wait_ms: 20_000,
            hello_cache_s: 60,
            task_open_timeout_secs: 180,
            ops_timeout_secs: 300,
            index_flush_ms: 1_000,
            max_slice_bytes: 2 * 1024 * 1024,
            ops_max_cost_usd: None,
            stage: StageConfig {
                confinement: Default::default(),
                docker_image: "gamecore-stage:6000.0.75f1-v1".into(),
                projects: Default::default(),
                command: None,
                slots: vec!["1".into()],
                timeout_s: 15 * 60,
            },
            voice: VoiceConfig {
                provider: "studio-voice".into(),
                instructions: "Transcribe the user's speech for a game editor prompt box. \
                               Do not answer."
                    .into(),
                max_sessions: 1,
            },
        }
    }

    /// The configuration of the supervised process: `ETOS_STATE_DIR`, `ETOS_AGENT`, the
    /// state directory's `config.toml`, then environment overrides.
    pub fn from_env() -> Result<Config, ConfigError> {
        let state_dir = std::env::var("ETOS_STATE_DIR")
            .map(PathBuf::from)
            .map_err(|_| ConfigError::Missing("ETOS_STATE_DIR"))?;
        let agent = std::env::var("ETOS_AGENT")
            .ok()
            .filter(|a| !a.is_empty())
            .unwrap_or_else(|| DEFAULT_AGENT.to_string());
        let mut cfg = Config::load(&agent, &state_dir)?;
        cfg.apply_env(|k| std::env::var(k).ok());
        if cfg.stage.command.is_none() {
            cfg.stage.command = discover_stage_command();
        }
        Ok(cfg)
    }

    /// Defaults plus `<state_dir>/config.toml` when present.
    pub fn load(agent: &str, state_dir: &Path) -> Result<Config, ConfigError> {
        let mut cfg = Config::defaults(agent, state_dir);
        let path = state_dir.join("config.toml");
        match std::fs::read_to_string(&path) {
            Ok(text) => {
                let file: FileConfig = toml::from_str(&text).map_err(|e| ConfigError::File {
                    path: path.display().to_string(),
                    message: e.to_string(),
                })?;
                cfg.apply_file(file);
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => {
                return Err(ConfigError::File {
                    path: path.display().to_string(),
                    message: e.to_string(),
                });
            }
        }
        Ok(cfg)
    }

    fn apply_file(&mut self, f: FileConfig) {
        if let Some(v) = f.allowed_apps {
            self.allowed_apps = v;
        }
        if let Some(v) = f.workers {
            self.workers = v;
        }
        if let Some(v) = f.default_worker {
            self.default_worker = v;
        }
        if let Some(v) = f.reask_on_invalid {
            self.reask_on_invalid = v;
        }
        self.port = f.port.or(self.port);
        self.schema = f.schema.or(self.schema.take());
        if let Some(v) = f.follow_wait_ms {
            self.follow_wait_ms = v;
        }
        if let Some(v) = f.hello_cache_s {
            self.hello_cache_s = v;
        }
        if let Some(v) = f.task_open_timeout_secs {
            self.task_open_timeout_secs = v.max(1);
        }
        if let Some(v) = f.ops_timeout_secs {
            self.ops_timeout_secs = v.max(1);
        }
        if let Some(v) = f.index_flush_ms {
            self.index_flush_ms = v.max(100);
        }
        if let Some(v) = f.max_slice_bytes {
            self.max_slice_bytes = v;
        }
        if let Some(v) = f.ops_max_cost_usd.filter(|v| v.is_finite() && *v >= 0.0) {
            self.ops_max_cost_usd = Some(v);
        }
        if let Some(s) = f.stage {
            if let Some(mode) = s.confinement {
                self.stage.confinement = mode;
            }
            if let Some(image) = s.docker_image {
                self.stage.docker_image = image;
            }
            if let Some(projects) = s.projects {
                self.stage.projects = projects;
            }
            self.stage.command = s.command.or(self.stage.command.take());
            if let Some(v) = s.slots.filter(|v| !v.is_empty()) {
                self.stage.slots = v;
            }
            if let Some(v) = s.timeout_s {
                self.stage.timeout_s = v;
            }
        }
        if let Some(v) = f.voice {
            if let Some(p) = v.provider {
                self.voice.provider = p;
            }
            if let Some(i) = v.instructions {
                self.voice.instructions = i;
            }
            if let Some(m) = v.max_sessions {
                self.voice.max_sessions = m.max(1);
            }
        }
    }

    /// Overrides: `GAMECORE_STUDIO_ALLOWED_APP` (comma-separated), `GAMECORE_STUDIO_STAGE_COMMAND`,
    /// `GAMECORE_STUDIO_SCHEMA`, `GAMECORE_STUDIO_PORT`.
    pub fn apply_env(&mut self, get: impl Fn(&str) -> Option<String>) {
        if let Some(v) = get("GAMECORE_STUDIO_ALLOWED_APP").filter(|v| !v.trim().is_empty()) {
            self.allowed_apps = v.split(',').map(|s| s.trim().to_string()).collect();
        }
        if let Some(v) = get("GAMECORE_STUDIO_STAGE_COMMAND").filter(|v| !v.is_empty()) {
            self.stage.command = Some(PathBuf::from(v));
        }
        if let Some(v) = get("GAMECORE_STUDIO_SCHEMA").filter(|v| !v.is_empty()) {
            self.schema = Some(PathBuf::from(v));
        }
        if let Some(p) = get("GAMECORE_STUDIO_PORT").and_then(|v| v.parse().ok()) {
            self.port = Some(p);
        }
    }
}

/// `studio/stage/stage.sh` of the game_core checkout the binary was built in, found by walking
/// up from the executable (an install `--link`ed from the checkout resolves there).
fn discover_stage_command() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?.canonicalize().ok()?;
    exe.ancestors()
        .map(|dir| dir.join("studio/stage/stage.sh"))
        .find(|p| p.is_file())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn file_and_env_override_defaults() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::write(
            dir.path().join("config.toml"),
            "reask_on_invalid = false\nport = 7451\n[stage]\nslots = [\"a\", \"b\"]\n[voice]\nmax_sessions = 2\n",
        )
        .unwrap();
        let mut cfg = Config::load("gamecore-studio", dir.path()).unwrap();
        assert!(!cfg.reask_on_invalid);
        assert_eq!(cfg.port, Some(7451));
        assert_eq!(cfg.stage.slots, vec!["a", "b"]);
        assert_eq!(cfg.voice.max_sessions, 2);
        cfg.apply_env(|k| (k == "GAMECORE_STUDIO_ALLOWED_APP").then(|| "a, b".to_string()));
        assert_eq!(cfg.allowed_apps, vec!["a", "b"]);
    }

    #[test]
    fn unknown_keys_are_errors() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::write(dir.path().join("config.toml"), "bogus = 1\n").unwrap();
        assert!(Config::load("gamecore-studio", dir.path()).is_err());
    }
}
