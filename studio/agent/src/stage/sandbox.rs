//! D3 execution boundary. Docker is required unless the operator explicitly selects host.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use super::pipeline::{ChildOutcome, run_child};

/// Operator-controlled confinement mode. Never selected by a stage request.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Confinement {
    /// Fail closed if Docker or Unity licensing is unavailable.
    #[default]
    Docker,
    /// Explicitly degraded operator mode.
    Host,
}
impl Confinement {
    /// Stable wire name.
    pub fn name(self) -> &'static str {
        match self {
            Self::Docker => "docker",
            Self::Host => "host",
        }
    }
}

/// Trusted sandbox launch specification, never accepted from candidates.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Sandbox {
    /// Mode.
    pub mode: Confinement,
    /// Pre-provisioned image with dotnet, Python and Unity runtime dependencies.
    pub image: String,
    /// Unity installation (read-only).
    pub editor: PathBuf,
    /// Only the Unity licensing directories, read-only; never mount a home/config directory.
    pub licences: Vec<PathBuf>,
    /// The single job slot.
    pub slot: PathBuf,
    /// Versioned cache.
    pub cache: PathBuf,
    /// Trusted package sources, read-only, never the live project.
    pub packages: PathBuf,
}
impl Sandbox {
    /// Defaults from fixed host installation conventions, not inherited tool overrides.
    pub fn defaults(slot: &Path, cache: &Path, repo: &Path) -> Self {
        let home = PathBuf::from(std::env::var_os("HOME").unwrap_or_default());
        Self {
            mode: Confinement::Docker,
            image: "gamecore-stage:6000.0.75f1-v1".into(),
            editor: home.join("Unity/Hub/Editor/6000.0.75f1/Editor"),
            licences: vec![
                home.join(".local/share/unity3d/Unity"),
                PathBuf::from("/var/lib/unity"),
            ],
            slot: slot.to_path_buf(),
            cache: cache.to_path_buf(),
            packages: repo.join("Packages"),
        }
    }

    fn container_name(&self) -> String {
        format!(
            "gc-stage-{}",
            crate::util::sha256_hex(self.slot.to_string_lossy().as_bytes())
        )
    }

    /// Stop only this slot's deterministic container, including after a wrapper timeout.
    pub fn stop_container(&self) {
        if self.mode == Confinement::Docker {
            let _ = Command::new("/usr/bin/docker")
                .args(["rm", "-f", &self.container_name()])
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .status();
        }
    }

    /// Build a command with no inherited environment, network, host HOME or live project.
    pub fn command(&self, executable: &Path) -> Result<Command, String> {
        std::fs::create_dir_all(self.slot.join("home")).map_err(|e| e.to_string())?;
        std::fs::create_dir_all(&self.cache).map_err(|e| e.to_string())?;
        let mut cmd;
        if self.mode == Confinement::Docker {
            cmd = Command::new("/usr/bin/docker");
            cmd.args([
                "run",
                "--rm",
                "--pull=never",
                "--network",
                "none",
                "--read-only",
                "--cap-drop=ALL",
                "--security-opt=no-new-privileges",
                "--pids-limit=512",
            ]);
            cmd.arg("--name").arg(self.container_name());
            use std::os::unix::fs::MetadataExt;
            let metadata = std::fs::metadata(&self.slot).map_err(|e| e.to_string())?;
            cmd.arg("--user")
                .arg(format!("{}:{}", metadata.uid(), metadata.gid()));
            // No daemon socket, host /tmp, HOME, live project, or provider environment.
            cmd.args([
                "--env",
                &format!("HOME={}", self.slot.join("home").display()),
                "--env",
                "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                "--env",
                "DOTNET_CLI_TELEMETRY_OPTOUT=1",
                "--env",
                "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1",
                "--env",
                &format!("TMPDIR={}", self.slot.join("tmp").display()),
                "--env",
                &format!("NUGET_PACKAGES={}/nuget", self.cache.display()),
            ]);
            std::fs::create_dir_all(self.slot.join("tmp")).map_err(|e| e.to_string())?;
            for (path, writable) in [
                (&self.slot, true),
                (&self.cache, true),
                (&self.editor, false),
                (&self.packages, false),
            ] {
                if !path.is_absolute() || path.to_string_lossy().contains([',', ':']) {
                    return Err("sandbox paths must be absolute without mount delimiters".into());
                }
                cmd.arg("--mount").arg(format!(
                    "type=bind,src={},dst={}{}",
                    path.display(),
                    path.display(),
                    if writable { "" } else { ",readonly" }
                ));
            }
            cmd.arg("--mount").arg(format!(
                "type=bind,src={},dst=/tmp",
                self.slot.join("tmp").display()
            ));
            for path in self.licences.iter().filter(|p| p.is_dir()) {
                // The operator cannot accidentally expose arbitrary config/credential trees.
                if !path.ends_with("unity3d/Unity") && path != Path::new("/var/lib/unity") {
                    return Err("unsupported Unity licence directory".into());
                }
                let target = if path.ends_with("unity3d/Unity") {
                    self.slot.join("home/.local/share/unity3d/Unity")
                } else {
                    path.clone()
                };
                cmd.arg("--mount").arg(format!(
                    "type=bind,src={},dst={},readonly",
                    path.display(),
                    target.display()
                ));
            }
            cmd.arg("--workdir")
                .arg(&self.slot)
                .arg(&self.image)
                .arg(executable);
        } else {
            cmd = Command::new(executable);
            cmd.current_dir(&self.slot);
        }
        Ok(cmd)
    }

    /// Run the exact command in the selected boundary; all output is redacted on write.
    pub fn run(&self, original: &Command, log: &Path, timeout: Duration) -> ChildOutcome {
        let requested = Path::new(original.get_program());
        let executable = if self.mode == Confinement::Docker
            && requested.file_name().is_some_and(|n| n == "dotnet")
        {
            Path::new("dotnet")
        } else {
            requested
        };
        let mut cmd = match self.command(executable) {
            Ok(cmd) => cmd,
            Err(e) => {
                return ChildOutcome {
                    code: None,
                    timed_out: false,
                    output: format!("sandbox_unavailable: {e}"),
                    elapsed: Duration::ZERO,
                };
            }
        };
        cmd.args(original.get_args());
        let home = self.slot.join("home").display().to_string();
        let out = run_child(&mut cmd, &[("HOME", &home)], log, timeout);
        self.stop_container();
        out
    }

    /// Run through the repository host-wide Editor allocator, using an engine wrapper
    /// which strips the raw log path and streams through the redactor.
    pub fn run_unity(
        &self,
        batch: &Path,
        project: &Path,
        label: &str,
        args: &[String],
        timeout: Duration,
    ) -> ChildOutcome {
        use std::os::unix::fs::PermissionsExt;
        let prepare = || -> Result<PathBuf, String> {
            std::fs::create_dir_all(&self.slot).map_err(|e| e.to_string())?;
            // Launch authority must be outside every candidate-writable mount.
            let launch = self
                .slot
                .parent()
                .ok_or("slot parent required")?
                .join(".launch")
                .join(crate::util::sha256_hex(
                    self.slot.to_string_lossy().as_bytes(),
                ));
            std::fs::create_dir_all(&launch).map_err(|e| e.to_string())?;
            let config = launch.join("sandbox-launch.json");
            std::fs::write(
                &config,
                serde_json::to_vec(self).map_err(|e| e.to_string())?,
            )
            .map_err(|e| e.to_string())?;
            let wrapper = launch.join("unity-confined");
            let quote =
                |p: &Path| format!("'{}'", p.display().to_string().replace('\'', "'\"'\"'"));
            let exe = std::env::current_exe().map_err(|e| e.to_string())?;
            std::fs::write(
                &wrapper,
                format!(
                    "#!/bin/sh\nexec {} stage sandbox-unity {} \"$@\"\n",
                    quote(&exe),
                    quote(&config)
                ),
            )
            .map_err(|e| e.to_string())?;
            std::fs::set_permissions(&wrapper, std::fs::Permissions::from_mode(0o700))
                .map_err(|e| e.to_string())?;
            Ok(wrapper)
        };
        let wrapper = match prepare() {
            Ok(p) => p,
            Err(e) => {
                return ChildOutcome {
                    code: None,
                    timed_out: false,
                    output: e,
                    elapsed: Duration::ZERO,
                };
            }
        };
        let home = std::env::var("HOME").unwrap_or_default();
        let mut cmd = Command::new("/bin/bash");
        cmd.arg(batch)
            .arg("--project")
            .arg(project)
            .arg("--log-dir")
            .arg(self.slot.join("out/logs"))
            .arg("--label")
            .arg(label)
            .arg("--timeout")
            .arg(timeout.as_secs().to_string())
            .args(["--attempts", "2", "--"])
            .args(args);
        let out = run_child(
            &mut cmd,
            &[("UNITY", &wrapper.display().to_string()), ("HOME", &home)],
            &self.slot.join(format!("{label}-allocator.log")),
            timeout.saturating_mul(2) + Duration::from_secs(30),
        );
        self.stop_container();
        out
    }

    /// Fail closed even if the engine exits zero without a completed licence handshake.
    pub fn probe(&self) -> Result<String, String> {
        if self.mode == Confinement::Host {
            return Ok("operator opted into host confinement".into());
        }
        let project = self.slot.join("probe-project");
        std::fs::create_dir_all(project.join("ProjectSettings")).map_err(|e| e.to_string())?;
        std::fs::create_dir_all(project.join("Packages")).map_err(|e| e.to_string())?;
        std::fs::write(
            project.join("Packages/manifest.json"),
            "{\"dependencies\":{}}",
        )
        .map_err(|e| e.to_string())?;
        let batch = self
            .packages
            .parent()
            .ok_or("no repo parent")?
            .join("studio/tools/unity-batch.sh");
        let out = self.run_unity(
            &batch,
            &project,
            "sandbox-probe",
            &["-quit".into()],
            Duration::from_secs(90),
        );
        if out.ok()
            && out.output.contains("Batchmode quit successfully invoked")
            && !out.output.contains("No valid Unity Editor license")
        {
            Ok(out.output)
        } else {
            Err(format!(
                "sandbox_unavailable: Unity container probe exit {:?}: {}",
                out.code, out.output
            ))
        }
    }
}

/// Host unity-batch invokes this wrapper, preserving its global allocation protocol. The
/// engine writes only to stdout; the wrapper writes the selected Unity log after redaction.
pub fn unity_wrapper(config: &Path, args: &[String]) -> Result<i32, String> {
    let sandbox: Sandbox =
        serde_json::from_slice(&std::fs::read(config).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let mut cmd = Command::new(sandbox.editor.join("Unity"));
    let mut log = None;
    let mut it = args.iter();
    while let Some(arg) = it.next() {
        if arg == "-logFile" {
            log = it.next().map(PathBuf::from);
        } else {
            cmd.arg(arg);
        }
    }
    cmd.args(["-logFile", "-"]);
    let capture = log
        .clone()
        .unwrap_or_else(|| sandbox.slot.join("unity-stream.log"));
    let out = sandbox.run(&cmd, &capture, Duration::from_secs(1800));
    if let Some(log) = log {
        std::fs::write(log, &out.output).map_err(|e| e.to_string())?;
    }
    print!("{}", out.output);
    Ok(out.code.unwrap_or(124))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn r2_11_docker_command_has_no_live_project_or_host_home() {
        let temp = tempfile::tempdir().unwrap();
        let sandbox = Sandbox::defaults(
            &temp.path().join("slot"),
            &temp.path().join("cache"),
            Path::new("/trusted"),
        );
        let cmd = sandbox.command(Path::new("dotnet")).unwrap();
        let args = cmd
            .get_args()
            .map(|s| s.to_string_lossy())
            .collect::<Vec<_>>()
            .join(" ");
        for required in [
            "--network none",
            "--read-only",
            "readonly",
            "--cap-drop=ALL",
            "HOME=",
        ] {
            assert!(args.contains(required), "{args}");
        }
        assert!(!args.contains("docker.sock"));
        assert!(!args.contains("games/hollowmere"));
        assert_eq!(Confinement::default(), Confinement::Docker);
    }
}
