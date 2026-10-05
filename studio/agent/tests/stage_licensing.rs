#![allow(clippy::unwrap_used, clippy::expect_used)]
//! Candidate-free host acceptance for the default Docker launcher; no ETOS service or keys.
use std::path::{Path, PathBuf};
use std::process::Command;

use gamecore_studio::stage::sandbox::Sandbox;

#[test]
#[ignore = "myubuntu only: installed Unity entitlement and gamecore-stage Docker image; host allocator"]
fn r2_f2_default_docker_launcher_licenses_offline_and_removes_private_state() {
    let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .unwrap();
    let root =
        PathBuf::from(std::env::var_os("HOME").unwrap()).join(".cache/gamecore-studio/r2-f2");
    std::fs::create_dir_all(&root).unwrap();
    let job = tempfile::Builder::new()
        .prefix("licensing-acceptance-")
        .tempdir_in(root)
        .unwrap();
    let sandbox = Sandbox::defaults(&job.path().join("slot"), &job.path().join("cache"), repo);
    let config = job.path().join("sandbox.json");
    std::fs::write(&config, serde_json::to_vec(&sandbox).unwrap()).unwrap();
    let output = Command::new(env!("CARGO_BIN_EXE_gamecore-studio"))
        .args(["stage", "sandbox-probe"])
        .arg(config)
        .current_dir(job.path())
        .output()
        .unwrap();
    // Do not dump licence logs on assertion failure; the allocator retains redacted logs.
    assert!(
        output.status.success(),
        "licensing probe exit {:?}",
        output.status.code()
    );
    let output = String::from_utf8_lossy(&output.stdout);
    assert!(output.contains("[Licensing::Client] Successfully resolved entitlement details"));
    assert!(output.contains("Batchmode quit successfully invoked"));
    assert!(!output.contains("No valid Unity Editor license"));
    assert!(!job.path().join("-").exists());
    assert!(sandbox.slot.join("unity-stream.log").is_file());
    assert!(std::fs::read_dir(&sandbox.slot).unwrap().all(|entry| {
        !entry
            .unwrap()
            .file_name()
            .to_string_lossy()
            .starts_with(".licensing-")
    }));
}
