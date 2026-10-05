#![allow(clippy::unwrap_used, clippy::expect_used)]
//! Attempt streaming and interruption through the real trusted CLI wrapper.
use gamecore_studio::stage::sandbox::{Confinement, Sandbox};
use std::io::{BufRead, BufReader};
use std::os::unix::fs::PermissionsExt;
use std::path::Path;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

#[test]
fn stage_tmp2_streams_before_exit_and_retains_interrupted_attempts() {
    let temp = tempfile::tempdir().unwrap();
    let mut sandbox = Sandbox::defaults(
        &temp.path().join("slot"),
        &temp.path().join("cache"),
        Path::new("/trusted"),
    );
    sandbox.mode = Confinement::Host;
    sandbox.editor = temp.path().join("editor");
    std::fs::create_dir_all(&sandbox.editor).unwrap();
    std::fs::create_dir_all(&sandbox.slot).unwrap();
    let engine = sandbox.editor.join("Unity");
    std::fs::write(
        &engine,
        "#!/bin/sh\nprintf 'progress Bearer synthetic-test-secret\\n'\nsleep 30\n",
    )
    .unwrap();
    std::fs::set_permissions(&engine, std::fs::Permissions::from_mode(0o700)).unwrap();
    let config = temp.path().join("sandbox.json");
    std::fs::write(&config, serde_json::to_vec(&sandbox).unwrap()).unwrap();
    for attempt in 1..=2 {
        let started = Instant::now();
        let mut child = Command::new(env!("CARGO_BIN_EXE_gamecore-studio"))
            .args(["stage", "sandbox-unity"])
            .arg(&config)
            .stdout(Stdio::piped())
            .spawn()
            .unwrap();
        let mut line = String::new();
        BufReader::new(child.stdout.take().unwrap())
            .read_line(&mut line)
            .unwrap();
        assert!(started.elapsed() < Duration::from_secs(5));
        assert!(child.try_wait().unwrap().is_none());
        assert!(line.contains("progress Bearer [redacted]"));
        assert!(!line.contains("synthetic-test-secret"));
        Command::new("kill")
            .args(["-TERM", &child.id().to_string()])
            .status()
            .unwrap();
        assert!(!child.wait().unwrap().success());
        assert!(started.elapsed() < Duration::from_secs(8));
        let retained =
            std::fs::read_to_string(sandbox.slot.join(format!("unity-stream-a{attempt}.log")))
                .unwrap();
        assert_eq!(retained, line);
    }
    assert_eq!(
        std::fs::read(sandbox.slot.join("unity-stream-a1.log")).unwrap(),
        std::fs::read(sandbox.slot.join("unity-stream-a2.log")).unwrap()
    );
}

#[test]
#[ignore = "real Docker; STAGE_TMP_CACHE is a private provisioned cache; no real Unity"]
fn stage_tmp2_timeout_removes_attempt_container_before_next_attempt() {
    let temp = tempfile::tempdir().unwrap();
    let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .unwrap();
    let cache = std::path::PathBuf::from(std::env::var_os("STAGE_TMP_CACHE").unwrap());
    let mut sandbox = Sandbox::defaults(&temp.path().join("slot"), &cache, repo);
    sandbox.editor = temp.path().join("editor");
    std::fs::create_dir_all(&sandbox.editor).unwrap();
    std::fs::create_dir_all(&sandbox.slot).unwrap();
    let engine = sandbox.editor.join("Unity");
    std::fs::write(&engine, "#!/bin/sh\necho attempt-started\nsleep 30\n").unwrap();
    std::fs::set_permissions(&engine, std::fs::Permissions::from_mode(0o700)).unwrap();
    let config = temp.path().join("sandbox.json");
    std::fs::write(&config, serde_json::to_vec(&sandbox).unwrap()).unwrap();
    let first_log = sandbox.slot.join("unity-stream-a1.log");
    let mut first_bytes = Vec::new();
    for attempt in 1..=2 {
        if attempt == 2 {
            std::fs::write(&engine, "#!/bin/sh\necho second-attempt-clean\n").unwrap();
        }
        let output = Command::new("python3")
            .arg(repo.join("studio/stage/run-redacted.py"))
            .arg("--log")
            .arg(temp.path().join(format!("outer-{attempt}.log")))
            .args([
                "--timeout",
                "10",
                "--silence",
                "600",
                "--",
                env!("CARGO_BIN_EXE_gamecore-studio"),
                "stage",
                "sandbox-unity",
            ])
            .arg(&config)
            .output()
            .unwrap();
        assert_eq!(
            output.status.code(),
            Some(if attempt == 1 { 124 } else { 0 })
        );
        let name = format!(
            "gc-stage-{}-a{attempt}",
            &gamecore_studio::util::sha256_hex(sandbox.slot.to_string_lossy().as_bytes())[..32]
        );
        assert!(
            !Command::new("docker")
                .args(["inspect", &name])
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .status()
                .unwrap()
                .success()
        );
        if attempt == 1 {
            first_bytes = std::fs::read(&first_log).unwrap();
            assert!(String::from_utf8_lossy(&first_bytes).contains("attempt-started"));
        } else {
            assert_eq!(std::fs::read(&first_log).unwrap(), first_bytes);
            assert!(
                std::fs::read_to_string(sandbox.slot.join("unity-stream-a2.log"))
                    .unwrap()
                    .contains("second-attempt-clean")
            );
        }
    }
}
