//! D3 execution boundary. Docker is required unless the operator explicitly selects host.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use super::pipeline::{ChildOutcome, run_child};

mod licensing;
use licensing::LicenseHome;

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
    /// Host HOME path preserved inside Docker, backed only by a private job copy.
    pub home: PathBuf,
    /// Hostname used by Unity machine-bound entitlement validation.
    pub hostname: String,
    /// Unity licensing sources copied privately; never mounted from the host.
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
            home: home.clone(),
            hostname: std::fs::read_to_string("/proc/sys/kernel/hostname")
                .unwrap_or_default()
                .trim()
                .to_string(),
            licences: vec![
                home.join(".config/unity3d/Unity"),
                home.join(".local/share/unity3d/Unity"),
                PathBuf::from("/var/lib/unity"),
            ],
            slot: slot.to_path_buf(),
            cache: cache.to_path_buf(),
            packages: repo.join("Packages"),
        }
    }

    fn runtime_dir(&self) -> String {
        format!(
            "/tmp/gcs/{}",
            &crate::util::sha256_hex(self.slot.as_os_str().as_encoded_bytes())[..8]
        )
    }

    fn unity_arg(&self, path: &Path, project: Option<&Path>) -> Result<PathBuf, String> {
        let mapped = if Some(path) == project {
            PathBuf::from("/w/p")
        } else if let Ok(relative) = path.strip_prefix(&self.slot) {
            Path::new("/w/s").join(relative)
        } else {
            path.to_path_buf()
        };
        if mapped.is_absolute() && mapped.as_os_str().as_encoded_bytes().len() > 90 {
            return Err("sandbox_unavailable: Unity argument path exceeds 90 bytes".into());
        }
        Ok(mapped)
    }

    fn docker_config(&self) -> PathBuf {
        self.slot
            .parent()
            .unwrap_or(Path::new("/"))
            .join(".launch/docker-client")
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
                .env_clear()
                .envs(super::env::stage_env())
                .arg("--config")
                .arg(self.docker_config())
                .args(["rm", "-f", &self.container_name()])
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .status();
        }
    }

    /// Build a command with no inherited environment, network, host HOME or live project.
    fn command(&self, executable: &Path, private_home: &Path) -> Result<Command, String> {
        self.command_for_project(executable, private_home, None)
    }

    fn command_for_project(
        &self,
        executable: &Path,
        private_home: &Path,
        project: Option<&Path>,
    ) -> Result<Command, String> {
        for directory in [
            "home/.local/share/unity3d",
            "home/.cache/unity3d",
            "home/.config/unity3d/Unity",
        ] {
            std::fs::create_dir_all(self.slot.join(directory)).map_err(|e| e.to_string())?;
        }
        std::fs::create_dir_all(&self.cache).map_err(|e| e.to_string())?;
        let mut cmd;
        if self.mode == Confinement::Docker {
            let config = self.docker_config();
            std::fs::create_dir_all(&config).map_err(|e| e.to_string())?;
            cmd = Command::new("/usr/bin/docker");
            // The host Docker client must never load config/credential helpers written
            // into the candidate's HOME. This empty directory is outside all mounts.
            cmd.arg("--config").arg(config);
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
            if self.hostname.is_empty()
                || !self
                    .hostname
                    .chars()
                    .all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '-'))
            {
                return Err("sandbox requires a valid host hostname".into());
            }
            cmd.arg("--name").arg(self.container_name());
            cmd.arg("--hostname").arg(&self.hostname);
            use std::os::unix::fs::MetadataExt;
            let metadata = std::fs::metadata(&self.slot).map_err(|e| e.to_string())?;
            cmd.arg("--user")
                .arg(format!("{}:{}", metadata.uid(), metadata.gid()));
            if self.home.as_os_str().as_encoded_bytes().len() + "/.config/unity3d/Unity".len() > 90
            {
                return Err("Unity HOME-derived paths exceed 90 bytes".into());
            }
            let runtime = self.runtime_dir();
            // Every writable runtime/cache path is backed by this job or its disposable
            // licensing copy, never host /tmp or another job's Unity cache.
            use std::os::unix::fs::PermissionsExt;
            let runtime_host = self.slot.join(runtime.trim_start_matches('/'));
            licensing::no_links(&runtime_host)?;
            std::fs::create_dir_all(&runtime_host).map_err(|e| e.to_string())?;
            std::fs::set_permissions(&runtime_host, std::fs::Permissions::from_mode(0o700))
                .map_err(|e| e.to_string())?;
            std::fs::create_dir_all(private_home.join(".cache")).map_err(|e| e.to_string())?;
            // No daemon socket, host /tmp, HOME, live project, or provider environment.
            cmd.args([
                "--env",
                &format!("HOME={}", self.home.display()),
                "--env",
                "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                "--env",
                "DOTNET_CLI_TELEMETRY_OPTOUT=1",
                "--env",
                "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1",
                "--env",
                "DOTNET_PROCESSOR_COUNT=4",
                "--env",
                &format!("TMPDIR={runtime}"),
                "--env",
                &format!("XDG_RUNTIME_DIR={runtime}"),
                "--env",
                &format!("XDG_CACHE_HOME={runtime}/cache"),
                "--env",
                "NUGET_PACKAGES=/c/nuget",
                "--env",
                &format!("UPM_CACHE_ROOT={runtime}/cache/upm"),
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
            for (source, target, readonly) in [
                (self.slot.as_path(), "/w/s", false),
                (self.cache.as_path(), "/c", false),
                (self.editor.as_path(), "/u", true),
                (
                    private_home.join(".cache").as_path(),
                    &format!("{runtime}/cache"),
                    false,
                ),
            ] {
                licensing::mount_path(source)?;
                cmd.arg("--mount").arg(format!(
                    "type=bind,src={},dst={}{}",
                    source.display(),
                    target,
                    if readonly { ",readonly" } else { "" }
                ));
            }
            if let Some(project) = project {
                licensing::mount_path(project)?;
                licensing::no_links(project)?;
                if !project.starts_with(&self.slot) {
                    return Err("Unity project must belong to this job".into());
                }
                cmd.arg("--mount")
                    .arg(format!("type=bind,src={},dst=/w/p", project.display()));
            }
            licensing::mount_path(private_home)?;
            licensing::mount_path(&self.home)?;
            cmd.arg("--mount").arg(format!(
                "type=bind,src={},dst={}",
                private_home.display(),
                self.home.display()
            ));
            cmd.args([
                "--mount",
                "type=bind,src=/etc/machine-id,dst=/etc/machine-id,readonly",
            ]);
            if private_home.join(".system-unity").is_dir() {
                cmd.arg("--mount").arg(format!(
                    "type=bind,src={},dst=/var/lib/unity",
                    private_home.join(".system-unity").display()
                ));
            }
            if private_home.join(".services-config.json").is_file() {
                cmd.arg("--mount").arg(format!(
                    "type=bind,src={},dst=/usr/share/unity3d/config/services-config.json,readonly",
                    private_home.join(".services-config.json").display()
                ));
            }
            cmd.arg("--workdir")
                .arg("/w/s")
                .arg(&self.image)
                .arg(executable);
        } else {
            cmd = Command::new(executable);
            cmd.current_dir(&self.slot);
        }
        Ok(cmd)
    }

    /// Verify both the pinned archives and expanded compiler inputs before launching Docker.
    pub fn verify_cache(&self) -> Result<(), String> {
        let script = self
            .packages
            .parent()
            .ok_or("trusted repository missing")?
            .join("studio/stage/cache.py");
        let output = Command::new("/usr/bin/python3")
            .env_clear()
            .envs(super::env::stage_env())
            .arg(script)
            .arg(&self.cache)
            .arg("--verify")
            .output()
            .map_err(|e| e.to_string())?;
        if output.status.success() {
            Ok(())
        } else {
            Err("cache_invalid: provision the exact versioned cache with studio/stage/provision-cache.sh".into())
        }
    }

    /// Run the exact command in the selected boundary; all output is redacted on write.
    pub fn run(&self, original: &Command, log: &Path, timeout: Duration) -> ChildOutcome {
        let verified = match self.mode {
            Confinement::Docker => self.verify_cache(),
            Confinement::Host => Ok(()),
        };
        if let Err(error) = verified {
            return ChildOutcome {
                code: None,
                timed_out: false,
                output: error,
                elapsed: Duration::ZERO,
            };
        }
        let requested = Path::new(original.get_program());
        let executable = if self.mode == Confinement::Docker
            && requested.file_name().is_some_and(|n| n == "dotnet")
        {
            Path::new("dotnet")
        } else {
            requested
        };
        let private_home = if self.mode == Confinement::Docker {
            match LicenseHome::prepare(self) {
                Ok(home) => Some(home),
                Err(error) => {
                    return ChildOutcome {
                        code: None,
                        timed_out: false,
                        output: format!("sandbox_unavailable: {error}"),
                        elapsed: Duration::ZERO,
                    };
                }
            }
        } else {
            None
        };
        let home_path = private_home
            .as_ref()
            .map(|h| h.0.as_path())
            .unwrap_or(&self.slot);
        let unity = self.mode == Confinement::Docker && requested == self.editor.join("Unity");
        let original_args: Vec<_> = original.get_args().collect();
        let project = if unity {
            original_args
                .windows(2)
                .find(|p| p[0] == "-projectPath")
                .map(|p| Path::new(p[1]))
        } else {
            None
        };
        if unity && let Err(error) = licensing::copy_unity_cache(self, home_path) {
            return ChildOutcome {
                code: None,
                timed_out: false,
                output: error,
                elapsed: Duration::ZERO,
            };
        }
        let mut cmd = match if unity {
            self.command_for_project(Path::new("/u/Unity"), home_path, project)
        } else {
            self.command(executable, home_path)
        } {
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
        for arg in original.get_args() {
            if unity {
                let mapped = match self.unity_arg(Path::new(arg), project) {
                    Ok(path) => path,
                    Err(error) => {
                        return ChildOutcome {
                            code: None,
                            timed_out: false,
                            output: error,
                            elapsed: Duration::ZERO,
                        };
                    }
                };
                cmd.arg(mapped);
            } else {
                cmd.arg(arg);
            }
        }
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
            .args(["--attempts", "2"])
            .args(batch_test_args(args));
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
        // Fail before acquiring an Editor allocation when provisioning is missing.
        self.verify_cache()?;
        let project = self.slot.join("probe-project");
        std::fs::create_dir_all(project.join("Assets")).map_err(|e| e.to_string())?;
        std::fs::create_dir_all(project.join("ProjectSettings")).map_err(|e| e.to_string())?;
        let version = self
            .editor
            .parent()
            .and_then(Path::file_name)
            .and_then(|s| s.to_str())
            .ok_or("Unity installation must include its version directory")?;
        std::fs::write(
            project.join("ProjectSettings/ProjectVersion.txt"),
            format!("m_EditorVersion: {version}\n"),
        )
        .map_err(|e| e.to_string())?;
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
        let engine_log = self.slot.join("unity-stream.log");
        let _ = std::fs::remove_file(&engine_log);
        let out = self.run_unity(
            &batch,
            &project,
            "sandbox-probe",
            &["-quit".into()],
            Duration::from_secs(90),
        );
        let engine_output = std::fs::read_to_string(engine_log).unwrap_or_default();
        if probe_passed(&out, &engine_output) {
            Ok(format!("{}\n{}", out.output, engine_output))
        } else {
            Err(format!(
                "sandbox_unavailable: Unity container probe exit {:?}: {}",
                out.code,
                format_args!("{}\n{}", out.output, engine_output)
            ))
        }
    }
}

// The host allocator owns the results path; it rejects raw -testResults after --.
fn batch_test_args(args: &[String]) -> Vec<String> {
    let mut wrapper = Vec::new();
    let mut engine = Vec::new();
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        if arg.eq_ignore_ascii_case("-testResults") {
            wrapper.push("--results".into());
            if let Some(path) = args.next() {
                wrapper.push(path.clone());
            }
        } else {
            engine.push(arg.clone());
        }
    }
    wrapper.push("--".into());
    wrapper.extend(engine);
    wrapper
}

fn probe_passed(out: &ChildOutcome, engine_output: &str) -> bool {
    out.ok()
        && engine_output.contains("Batchmode quit successfully invoked")
        && engine_output.contains("[Licensing::Client] Successfully resolved entitlement details")
        && !engine_output.contains("No valid Unity Editor license")
}

/// Host unity-batch invokes this wrapper, preserving its global allocation protocol. The
/// engine writes only to stdout; the wrapper writes the selected Unity log after redaction.
pub fn unity_wrapper(config: &Path, args: &[String]) -> Result<i32, String> {
    let sandbox: Sandbox =
        serde_json::from_slice(&std::fs::read(config).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let mut cmd = Command::new(sandbox.editor.join("Unity"));
    if sandbox.mode == Confinement::Docker {
        // Host core counts otherwise create hundreds of compiler threads and exhaust
        // the unchanged 512-PID boundary. The outer job deadline remains authoritative.
        cmd.args(["-job-worker-count", "4", "-diag-debug-shader-compiler"]);
    }
    let mut log = None;
    let mut it = args.iter();
    while let Some(arg) = it.next() {
        if arg == "-logFile" {
            log = it
                .next()
                .filter(|value| value.as_str() != "-")
                .map(PathBuf::from);
        } else {
            cmd.arg(arg);
        }
    }
    cmd.args(["-logFile", "-"]);
    let capture = log
        .clone()
        .unwrap_or_else(|| sandbox.slot.join("unity-stream.log"));
    let out = sandbox.run(&cmd, &capture, Duration::from_secs(1800));
    // run_child consumes its scratch file; retain the redacted engine stream for the
    // readiness check even when unity-batch requested stdout via -logFile -.
    std::fs::write(&capture, &out.output).map_err(|e| e.to_string())?;
    print!("{}", out.output);
    Ok(out.code.unwrap_or(124))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn stage_tmp_unity_paths_are_short_and_job_private() {
        use std::os::unix::fs::{MetadataExt, PermissionsExt};
        let temp = tempfile::tempdir().unwrap();
        let owner = temp.path().join("a".repeat(64)).join("b".repeat(64));
        let sandbox = Sandbox::defaults(
            &owner.join("job-12345678"),
            &owner.join("_warm/cache"),
            Path::new("/trusted"),
        );
        assert!(sandbox.slot.as_os_str().as_encoded_bytes().len() > 108);
        let project = sandbox.slot.join("project");
        std::fs::create_dir_all(&project).unwrap();
        let private = sandbox.slot.join(".licensing-test");
        let mut command = sandbox
            .command_for_project(Path::new("/u/Unity"), &private, Some(&project))
            .unwrap();
        for arg in [
            Path::new("-projectPath"),
            &project,
            Path::new("-testResults"),
            &sandbox.slot.join("out/editmode.xml"),
            Path::new("-logFile"),
            Path::new("-"),
        ] {
            command.arg(sandbox.unity_arg(arg, Some(&project)).unwrap());
        }
        let args: Vec<_> = command
            .get_args()
            .map(|s| s.to_string_lossy().into_owned())
            .collect();
        let engine = args.iter().position(|a| a == "/u/Unity").unwrap();
        for path in &args[engine..] {
            if path.starts_with('/') {
                assert!(path.len() <= 90, "{path}");
            }
        }
        for pair in args.windows(2).filter(|p| p[0] == "--env") {
            let (_, value) = pair[1].split_once('=').unwrap();
            if value.starts_with('/') && !pair[1].starts_with("PATH=") {
                assert!(value.len() <= 90, "{}", pair[1]);
            }
        }
        assert!(args.iter().any(|a| a == "/w/p"));
        assert!(args.iter().any(|a| a == "/w/s/out/editmode.xml"));
        let runtime_host = sandbox
            .slot
            .join(sandbox.runtime_dir().trim_start_matches('/'));
        assert_eq!(
            std::fs::metadata(&runtime_host)
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o700
        );
        assert_eq!(
            std::fs::metadata(&runtime_host).unwrap().uid(),
            std::fs::metadata(&sandbox.slot).unwrap().uid()
        );
        let other = Sandbox::defaults(
            &owner.join("job-other"),
            &sandbox.cache,
            Path::new("/trusted"),
        );
        assert_ne!(sandbox.runtime_dir(), other.runtime_dir());
        assert!(
            sandbox
                .unity_arg(&sandbox.slot.join("x".repeat(91)), Some(&project))
                .is_err()
        );
        assert!(
            sandbox
                .command_for_project(Path::new("/u/Unity"), &private, Some(Path::new("/outside")))
                .is_err()
        );
    }

    #[test]
    fn stage_tmp_short_mounts_refuse_symlink_escapes() {
        use std::os::unix::fs::symlink;
        let temp = tempfile::tempdir().unwrap();
        let sandbox = Sandbox::defaults(
            &temp.path().join("slot"),
            &temp.path().join("cache"),
            Path::new("/trusted"),
        );
        std::fs::create_dir_all(&sandbox.slot).unwrap();
        let outside = temp.path().join("outside");
        std::fs::create_dir(&outside).unwrap();
        symlink(&outside, sandbox.slot.join("tmp")).unwrap();
        assert!(
            sandbox
                .command(Path::new("dotnet"), &temp.path().join("private"))
                .is_err()
        );
        std::fs::remove_file(sandbox.slot.join("tmp")).unwrap();
        symlink(&outside, sandbox.slot.join("project")).unwrap();
        assert!(
            sandbox
                .command_for_project(
                    Path::new("/u/Unity"),
                    &temp.path().join("private"),
                    Some(&sandbox.slot.join("project"))
                )
                .is_err()
        );
        assert!(std::fs::read_dir(outside).unwrap().next().is_none());
    }

    #[test]
    fn r2_11_stage_int_results_are_owned_by_host_allocator() {
        let args = [
            "-runTests",
            "-testPlatform",
            "EditMode",
            "-testResults",
            "/slot/out/editmode.xml",
        ];
        assert_eq!(
            batch_test_args(&args.map(str::to_string)),
            [
                "--results",
                "/slot/out/editmode.xml",
                "--",
                "-runTests",
                "-testPlatform",
                "EditMode"
            ]
        );
    }

    #[test]
    fn r2_11_stage_int_launcher_refuses_missing_cache_before_execution() {
        let dir = tempfile::tempdir().unwrap();
        let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
            .ancestors()
            .nth(2)
            .unwrap();
        let sandbox = Sandbox::defaults(&dir.path().join("slot"), &dir.path().join("cache"), repo);
        let out = sandbox.run(
            &Command::new("must-never-execute"),
            &dir.path().join("log"),
            Duration::from_secs(1),
        );
        assert!(!out.ok());
        assert!(out.output.contains("cache_invalid"));
        assert!(!sandbox.slot.exists());
        assert!(sandbox.probe().unwrap_err().contains("cache_invalid"));
        assert!(!sandbox.slot.exists());
    }

    #[test]
    fn r2_f2_licensing_command_mounts_only_private_state_and_fixed_identity() {
        use std::os::unix::fs::MetadataExt;
        let temp = tempfile::tempdir().unwrap();
        let mut sandbox = Sandbox::defaults(
            &temp.path().join("slot"),
            &temp.path().join("cache"),
            Path::new("/trusted"),
        );
        sandbox.home = PathBuf::from("/home/creator");
        sandbox.hostname = "build-host".into();
        sandbox.editor = PathBuf::from("/home/creator/Unity/Editor");
        let private = temp.path().join("private");
        let cmd = sandbox.command(Path::new("dotnet"), &private).unwrap();
        let args: Vec<_> = cmd
            .get_args()
            .map(|s| s.to_string_lossy().into_owned())
            .collect();
        let mounts: Vec<_> = args
            .windows(2)
            .filter(|v| v[0] == "--mount")
            .map(|v| v[1].clone())
            .collect();
        assert_eq!(
            mounts,
            vec![
                format!("type=bind,src={0},dst={0}", sandbox.slot.display()),
                format!("type=bind,src={0},dst={0}", sandbox.cache.display()),
                format!(
                    "type=bind,src={0},dst={0},readonly",
                    sandbox.editor.display()
                ),
                "type=bind,src=/trusted/Packages,dst=/trusted/Packages,readonly".into(),
                format!(
                    "type=bind,src={},dst=/tmp",
                    sandbox.slot.join("tmp").display()
                ),
                format!("type=bind,src={},dst=/w/s", sandbox.slot.display()),
                format!("type=bind,src={},dst=/c", sandbox.cache.display()),
                format!("type=bind,src={},dst=/u,readonly", sandbox.editor.display()),
                format!(
                    "type=bind,src={},dst={}/cache",
                    private.join(".cache").display(),
                    sandbox.runtime_dir()
                ),
                format!("type=bind,src={},dst=/home/creator", private.display()),
                "type=bind,src=/etc/machine-id,dst=/etc/machine-id,readonly".into(),
            ]
        );
        let metadata = std::fs::metadata(&sandbox.slot).unwrap();
        for pair in [
            vec!["--network".into(), "none".into()],
            vec!["--hostname".into(), "build-host".into()],
            vec![
                "--user".into(),
                format!("{}:{}", metadata.uid(), metadata.gid()),
            ],
            vec!["--env".into(), "HOME=/home/creator".into()],
            vec!["--env".into(), "DOTNET_PROCESSOR_COUNT=4".into()],
            vec![
                "--env".into(),
                format!("UPM_CACHE_ROOT={}/cache/upm", sandbox.runtime_dir()),
            ],
        ] {
            assert!(args.windows(2).any(|v| v == pair));
        }
        for flag in [
            "--read-only",
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",
            "--pull=never",
        ] {
            assert!(args.iter().any(|a| a == flag));
        }
        assert_eq!(cmd.get_envs().count(), 0);
    }

    #[test]
    fn r2_f2_probe_requires_engine_handshake_even_when_allocator_exits_zero() {
        let mut out = ChildOutcome {
            code: Some(0),
            timed_out: false,
            output: "RESULT sandbox-probe: PASS".into(),
            elapsed: Duration::ZERO,
        };
        let engine = "[Licensing::Client] Successfully resolved entitlement details\nBatchmode quit successfully invoked";
        assert!(probe_passed(&out, engine));
        assert!(!probe_passed(&out, ""));
        assert!(!probe_passed(&out, "Batchmode quit successfully invoked"));
        assert!(!probe_passed(
            &out,
            &format!("{engine}\nNo valid Unity Editor license")
        ));
        out.code = Some(1);
        assert!(!probe_passed(&out, engine));
        out.code = Some(0);
        out.timed_out = true;
        assert!(!probe_passed(&out, engine));
    }

    #[test]
    #[ignore = "requires the locally provisioned gamecore-stage Docker image; no Unity or ETOS"]
    fn r2_11_docker_isolation_blocks_host_files_environment_and_network() {
        let dir = tempfile::tempdir().unwrap();
        let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap();
        let sandbox = Sandbox::defaults(&dir.path().join("slot"), &dir.path().join("cache"), repo);
        assert!(
            Command::new(repo.join("studio/stage/provision-cache.sh"))
                .arg(&sandbox.cache)
                .arg("--offline-from")
                .arg(
                    PathBuf::from(std::env::var_os("HOME").unwrap())
                        .join(".cache/gamecore-studio/stage-int/bootstrap-nuget")
                )
                .status()
                .unwrap()
                .success()
        );
        std::fs::create_dir_all(&sandbox.slot).unwrap();
        let outside = dir.path().join("outside-sentinel");
        std::fs::write(&outside, "host-only").unwrap();
        let command = format!(
            "set -eu; test ! -e '{}'; test ! -e '{}'; test -z \"${{ETOS_STATE_DIR:-}}\"; touch '{}/allowed'; if touch /forbidden 2>/dev/null; then exit 9; fi; test \"$(wc -l < /proc/net/route)\" -eq 1; echo sandbox-isolation-passed",
            outside.display(),
            repo.join("games/hollowmere").display(),
            sandbox.slot.display()
        );
        let out = sandbox.run(
            Command::new("/bin/sh").args(["-c", &command]),
            &sandbox.slot.join("isolation.log"),
            Duration::from_secs(30),
        );
        assert!(out.ok(), "{}", out.output);
        assert!(out.output.contains("sandbox-isolation-passed"));
        assert!(sandbox.slot.join("allowed").is_file());
        assert_eq!(std::fs::read_to_string(outside).unwrap(), "host-only");
        let socket = sandbox.run(
            Command::new("/usr/bin/python3").args(["-c", r#"
import os, socket, stat
runtime = os.environ['TMPDIR']
assert runtime == os.environ['XDG_RUNTIME_DIR']
assert stat.S_IMODE(os.stat(runtime).st_mode) == 0o700
for name in ('TMPDIR', 'XDG_RUNTIME_DIR', 'XDG_CACHE_HOME', 'UPM_CACHE_ROOT', 'NUGET_PACKAGES', 'HOME'):
    assert len(os.environ[name].encode()) <= 90, name
assert os.getcwd() == '/w/s'
path = runtime + '/' + 'bee-' + 'x' * 64
with socket.socket(socket.AF_UNIX) as sock:
    sock.bind(path)
os.unlink(path)
print('short private runtime socket passed')
"#]),
            &sandbox.slot.join("socket.log"),
            Duration::from_secs(30),
        );
        assert!(socket.ok(), "{}", socket.output);
    }

    #[test]
    fn r2_11_docker_command_has_no_live_project_or_host_home() {
        let temp = tempfile::tempdir().unwrap();
        let sandbox = Sandbox::defaults(
            &temp.path().join("slot"),
            &temp.path().join("cache"),
            Path::new("/trusted"),
        );
        let cmd = sandbox
            .command(Path::new("dotnet"), &temp.path().join("private-home"))
            .unwrap();
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
        assert_eq!(cmd.get_args().next().unwrap(), "--config");
        assert!(!sandbox.docker_config().starts_with(&sandbox.slot));
        assert!(!sandbox.docker_config().starts_with(&sandbox.cache));
        assert!(!args.contains("docker.sock"));
        assert!(!args.contains("games/hollowmere"));
        assert_eq!(Confinement::default(), Confinement::Docker);
    }
}
