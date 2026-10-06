//! P4.2c request 4: real Python preflight and Rust mount binding share the same fixture.
use gamecore_studio::stage::pipeline::{SlotSource, StageOptions};
use serde_json::{Value, json};
use std::{fs, path::PathBuf, process::Command};

#[test]
fn r5_04_registered_project_pins_and_mount_share_root() {
    let fixture: Value =
        serde_json::from_str(include_str!("../../stage/tests/package-root-cases.json")).unwrap();
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path();
    let registered = root.join("registered");
    let project = root.join(fixture["project"].as_str().unwrap());
    fs::create_dir_all(project.join("Packages")).unwrap();
    fs::create_dir_all(project.join("ProjectSettings")).unwrap();
    assert!(
        Command::new("git")
            .args(["init", "-q"])
            .arg(&registered)
            .status()
            .unwrap()
            .success()
    );
    let package = fixture["package"].as_str().unwrap();
    for prefix in ["registered/Packages", "tools/Packages", "candidate"] {
        let dir = root.join(prefix).join(package);
        fs::create_dir_all(&dir).unwrap();
        fs::write(
            dir.join("package.json"),
            json!({"name":package}).to_string(),
        )
        .unwrap();
    }
    std::os::unix::fs::symlink(
        root.join("candidate").join(package),
        registered.join("Packages/linked"),
    )
    .unwrap();
    let repo = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    for case in fixture["cases"].as_array().unwrap() {
        let pin = case["pin"]
            .as_str()
            .unwrap()
            .replace("{root}", root.to_str().unwrap());
        fs::write(
            project.join("Packages/manifest.json"),
            json!({"dependencies":{package:pin}}).to_string(),
        )
        .unwrap();
        let mut opts = StageOptions::from_env(&repo, "root-test", SlotSource::Existing);
        opts.source_project = project.clone();
        let result = opts.bind_project();
        if case["ok"] == true {
            result.unwrap();
            assert_eq!(opts.sandbox.packages, registered.join("Packages"));
            assert_eq!(opts.repo, repo, "tools remain the trusted service checkout");
        } else {
            let error = result.unwrap_err();
            assert!(error.contains("stage_package_root_mismatch"), "{error}");
            assert!(error.contains("stage.projects"), "{error}");
            assert_eq!(
                opts.sandbox.packages,
                repo.join("Packages"),
                "refused pins never become mounts"
            );
        }
    }
}

#[test]
fn r5_04_revision_refuses_before_rebinding_mount() {
    let repo = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let mut opts = StageOptions::from_env(&repo, "revision-test", SlotSource::Existing);
    opts.source_project = repo.join("games/hollowmere");
    opts.expected_source_revision = Some("0".repeat(40));
    opts.sandbox.packages = PathBuf::from("/must-not-be-rebound");
    assert!(
        opts.bind_project()
            .unwrap_err()
            .contains("source revision changed")
    );
    assert_eq!(opts.sandbox.packages, PathBuf::from("/must-not-be-rebound"));
}

#[test]
fn r5_04_mount_mismatch_refuses_before_slot_creation() {
    use gamecore_studio::stage::pipeline::prepare_slot;
    use std::time::Duration;
    let repo = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let temp = tempfile::tempdir().unwrap();
    let mut opts = StageOptions::from_env(
        &repo,
        "mismatch",
        SlotSource::Candidate(temp.path().join("absent")),
    );
    opts.root = temp.path().join("slots");
    opts.source_project = repo.join("games/hollowmere");
    opts.sandbox.packages = temp.path().join("candidate-packages");
    assert!(
        prepare_slot(&opts, Duration::from_secs(10))
            .unwrap_err()
            .contains("stage_package_root_mismatch")
    );
    assert!(!opts.slot_dir().join("project").exists());
}

#[test]
fn r5_04_existing_slot_cannot_reuse_another_project_binding() {
    use gamecore_studio::stage::pipeline::run_stage;
    let repo = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let temp = tempfile::tempdir().unwrap();
    let mut opts = StageOptions::from_env(&repo, "existing", SlotSource::Existing);
    opts.root = temp.path().join("slots");
    opts.source_project = repo.join("games/hollowmere");
    fs::create_dir_all(opts.slot_dir()).unwrap();
    fs::write(
        opts.slot_dir().join("stage.json"),
        json!({"source":{"project":"/different/project","repo":repo},"manifest":{}}).to_string(),
    )
    .unwrap();
    let error = run_stage(&opts).unwrap_err();
    assert!(error.contains("stage_package_root_mismatch"), "{error}");
    assert!(error.contains("stage.projects"), "{error}");
    assert!(!opts.slot_dir().join("sandbox.json").exists());
}
