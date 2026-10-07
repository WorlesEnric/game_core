//! Job-private cancellation and child identities, outside candidate-writable mounts.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

/// Shared by the service pipeline and its trusted Unity wrapper. CLI runs have no journal.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Control {
    directory: Option<PathBuf>,
}

impl Control {
    /// A trusted service-owned job directory, never a candidate input.
    pub fn at(directory: PathBuf) -> Self {
        Self {
            directory: Some(directory),
        }
    }

    /// Persist the stop request before signalling children, including across restarts.
    pub fn cancel(&self) -> Result<(), String> {
        if let Some(dir) = &self.directory {
            std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
            let file = std::fs::File::create(dir.join("cancelled")).map_err(|e| e.to_string())?;
            file.sync_all().map_err(|e| e.to_string())?;
            std::fs::File::open(dir)
                .and_then(|f| f.sync_all())
                .map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    /// A stop request prevents every subsequent child launch and verdict.
    pub fn check(&self) -> Result<(), String> {
        if self.cancelled() {
            Err("stage_cancelled".into())
        } else {
            Ok(())
        }
    }

    /// True once the service durably requested cancellation.
    pub fn cancelled(&self) -> bool {
        self.directory
            .as_ref()
            .is_some_and(|dir| dir.join("cancelled").exists())
    }

    /// Exact Docker label value shared by every tool and Unity attempt of this job.
    pub fn identity(&self) -> Option<String> {
        self.directory
            .as_ref()
            .map(|dir| crate::util::sha256_hex(dir.as_os_str().as_encoded_bytes()))
    }

    pub(super) fn register(&self, pid: u32) -> Result<ChildRegistration, String> {
        let path = if let Some(dir) = &self.directory {
            std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
            let (identity, _) = process_identity(pid).ok_or("stage child identity unavailable")?;
            let path = dir.join(format!("{pid}.child"));
            std::fs::write(&path, identity).map_err(|e| e.to_string())?;
            Some(path)
        } else {
            None
        };
        Ok(ChildRegistration(path))
    }

    /// Recover only recorded process groups whose Linux start identity still matches.
    /// The cancellation marker also stops wrapper children spawned during the TERM grace.
    pub fn stop_children(&self) -> Result<(), String> {
        self.cancel()?;
        let started = Instant::now();
        loop {
            let children = self.children()?;
            if children.is_empty() {
                return Ok(());
            }
            let signal = if started.elapsed() < Duration::from_secs(15) {
                "TERM"
            } else {
                "KILL"
            };
            for pid in children {
                signal_group(pid, signal);
            }
            if started.elapsed() >= Duration::from_secs(20) {
                return Err("stage child teardown did not complete".into());
            }
            std::thread::sleep(Duration::from_millis(50));
        }
    }

    fn children(&self) -> Result<Vec<u32>, String> {
        let Some(dir) = &self.directory else {
            return Ok(Vec::new());
        };
        if !dir.exists() {
            return Ok(Vec::new());
        }
        let mut children = Vec::new();
        for entry in std::fs::read_dir(dir).map_err(|e| e.to_string())? {
            let path = entry.map_err(|e| e.to_string())?.path();
            if path.extension().is_none_or(|e| e != "child") {
                continue;
            }
            let pid = path
                .file_stem()
                .and_then(|s| s.to_str())
                .and_then(|s| s.parse::<u32>().ok());
            if let Some(pid) = pid {
                let saved = match std::fs::read_to_string(&path) {
                    Ok(saved) => saved,
                    Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
                    Err(error) => return Err(error.to_string()),
                };
                match process_identity(pid) {
                    Some((actual, zombie)) if actual == saved => {
                        if zombie {
                            signal_group(pid, "KILL");
                        }
                        if !zombie || group_has_live_members(pid)? {
                            children.push(pid);
                            continue;
                        }
                    }
                    None if group_has_live_members(pid)? => {
                        // A vanished group leader has no inspectable start identity. Never
                        // target a possibly reused group or acknowledge unproven cleanup.
                        return Err("stage child group identity unavailable; cancellation cleanup remains pending".into());
                    }
                    _ => {}
                }
            }
            match std::fs::remove_file(path) {
                Ok(()) => {}
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                Err(error) => return Err(error.to_string()),
            }
        }
        Ok(children)
    }
}

pub(super) struct ChildRegistration(Option<PathBuf>);
impl Drop for ChildRegistration {
    fn drop(&mut self) {
        if let Some(path) = &self.0 {
            let _ = std::fs::remove_file(path);
        }
    }
}

fn process_identity(pid: u32) -> Option<(String, bool)> {
    let stat = std::fs::read_to_string(format!("/proc/{pid}/stat")).ok()?;
    let mut fields = stat.rsplit_once(") ")?.1.split_whitespace();
    let zombie = fields.next()? == "Z";
    let start = fields.nth(18)?;
    let boot = std::fs::read_to_string("/proc/sys/kernel/random/boot_id").ok()?;
    Some((format!("{}:{start}", boot.trim()), zombie))
}

fn group_has_live_members(group: u32) -> Result<bool, String> {
    for entry in std::fs::read_dir("/proc").map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        if entry
            .file_name()
            .to_str()
            .and_then(|name| name.parse::<u32>().ok())
            .is_none()
        {
            continue;
        }
        let Ok(stat) = std::fs::read_to_string(entry.path().join("stat")) else {
            continue;
        };
        let Some((_, fields)) = stat.rsplit_once(") ") else {
            continue;
        };
        let mut fields = fields.split_whitespace();
        if fields.next() != Some("Z")
            && fields.nth(1).and_then(|s| s.parse::<u32>().ok()) == Some(group)
        {
            return Ok(true);
        }
    }
    Ok(false)
}

pub(super) fn signal_group(pid: u32, signal: &str) {
    let _ = Command::new("/bin/kill")
        .env_clear()
        .envs(super::env::stage_env())
        .args(["-s", signal, "--"])
        .arg(format!("-{pid}"))
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status();
}

/// Resolve the job control under installation state, not the slot or Unity project.
pub fn job_control(state: &Path, job: &str) -> Control {
    Control::at(state.join("stage-control").join(job))
}
