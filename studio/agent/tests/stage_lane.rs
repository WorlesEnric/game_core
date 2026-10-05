#![allow(clippy::unwrap_used, clippy::expect_used)]
//! The staging lane through the runner's request API, without Unity (host test: needs
//! python3, tar and this repository's `studio/stage` + `games/hollowmere`): a candidate in the
//! ledger is materialised into a slot, the scan and the checkers run (a partial verdict that
//! never passes), a forbidden candidate is caught by the scan, one slot holds one change set,
//! and `action: "discard"` removes the slot.

use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use gamecore_studio::config::StageConfig;
use gamecore_studio::events::EventHub;
use gamecore_studio::ledger::{CandidateRow, Ledger};
use gamecore_studio::stage::StageRunner;
use gamecore_studio::stage::verdict;
use gamecore_studio::store::ArtifactStore;
use serde_json::{Value, json};

fn repo() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .unwrap()
        .to_path_buf()
}

struct Lane {
    _dir: tempfile::TempDir,
    root: PathBuf,
    work: PathBuf,
    ledger: Arc<Ledger>,
    store: ArtifactStore,
    runner: Arc<StageRunner>,
}

fn lane() -> Lane {
    let dir = tempfile::tempdir().unwrap();
    let ledger = Arc::new(Ledger::open(&dir.path().join("ledger.sqlite")).unwrap());
    let hub = EventHub::new(ledger.clone()).unwrap();
    let store = ArtifactStore::new(dir.path().join("store"));
    let root = dir.path().join("slots");
    let runner = StageRunner::with_paths(
        StageConfig {
            command: None,
            slots: vec!["1".into()],
            timeout_s: 60,
        },
        ledger.clone(),
        hub,
        store.clone(),
        dir.path(),
        root.clone(),
        Some(repo()),
    );
    Lane {
        work: dir.path().join("work"),
        _dir: dir,
        root,
        ledger,
        store,
        runner,
    }
}

const PACKAGE: &str = "com.example.stagelane";

/// A tiny mechanism package as a candidate in the ledger; returns the package digest.
fn put_candidate(l: &Lane, cs: &str, code: &str) -> String {
    let pkg = l.work.join(cs).join("pkg");
    std::fs::create_dir_all(pkg.join("Runtime")).unwrap();
    std::fs::write(
        pkg.join("package.json"),
        serde_json::to_vec_pretty(&json!({
            "name": PACKAGE, "version": "0.1.0", "displayName": "Stage lane test",
            "description": "A package the stage lane test stages.", "unity": "6000.0", "dependencies": {}
        }))
        .unwrap(),
    )
    .unwrap();
    std::fs::write(
        pkg.join("Runtime/Example.StageLane.asmdef"),
        "{\n    \"name\": \"Example.StageLane\",\n    \"references\": [],\n    \"noEngineReferences\": true\n}\n",
    )
    .unwrap();
    std::fs::write(pkg.join("Runtime/Lane.cs"), code).unwrap();
    let tgz = l.work.join(cs).join("package.tgz");
    let st = std::process::Command::new("tar")
        .arg("-czf")
        .arg(&tgz)
        .arg("-C")
        .arg(&pkg)
        .arg(".")
        .status()
        .unwrap();
    assert!(st.success());
    let bytes = std::fs::read(&tgz).unwrap();
    let (sha, _) = l.store.put(&bytes, None).unwrap();
    let change_set = json!({
        "schema": "gamecore.studio.change-set/1",
        "id": cs,
        "operations": [{"opId": "op_01", "tool": "mechanism.propose",
                        "args": {"package": {"artifact": format!("sha256:{sha}")}, "stageInputs": []}}],
        "artifacts": [{"sha256": sha, "name": "package.tgz", "mediaType": "application/gzip", "bytes": bytes.len()}]
    });
    l.ledger
        .put_candidate(&CandidateRow {
            change_set_id: cs.into(),
            request_id: "req_test".into(),
            task_id: "task_test".into(),
            attempt: 1,
            change_set,
            artifacts: json!([{"sha256": sha, "name": "package.tgz", "mediaType": "application/gzip",
                               "bytes": bytes.len(), "producer": {}}]),
            diagnostics: json!([]),
            received_at: 1,
        })
        .unwrap();
    sha
}

async fn wait_job(l: &Lane, id: &str) -> Value {
    for _ in 0..3000 {
        let j = l.ledger.stage(id).unwrap();
        if j.state == "done" || j.state == "failed" {
            return serde_json::to_value(j).unwrap();
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    panic!("stage job {id} did not finish");
}

const CLEAN: &str = "#nullable enable\nnamespace Example.StageLane\n{\n    public static class Lane\n    {\n        public const int Steps = 3;\n\n        public static int Twice(int x) => x * 2;\n    }\n}\n";

const FORBIDDEN: &str = "#nullable enable\nnamespace Example.StageLane\n{\n    public static class Lane\n    {\n        private static int presses;\n\n        public static void Run()\n        {\n            presses++;\n            System.Diagnostics.Process.Start(\"curl\");\n        }\n    }\n}\n";

#[tokio::test(flavor = "multi_thread")]
async fn a_candidate_is_staged_scanned_and_checked_into_its_own_slot() {
    let l = lane();
    let cs = "cs_01JAPP0000000000000000SAN1";
    let sha = put_candidate(&l, cs, CLEAN);
    let answer = l
        .runner
        .request(json!({"changeSetId": cs, "steps": ["scan", "checkers"]}))
        .unwrap();
    assert_eq!(answer.status.as_u16(), 202, "{}", answer.body);
    assert_eq!(answer.body["packageRef"], sha);
    let slot = answer.body["slot"].as_str().unwrap().to_string();
    assert_eq!(slot, "cs-01japp0000000000000000san1");
    let job = wait_job(&l, answer.body["jobId"].as_str().unwrap()).await;
    assert_eq!(job["state"], "done", "{job}");
    let v = &job["verdict"];
    assert_eq!(v["schema"], verdict::VERDICT_SCHEMA);
    assert_eq!(v["partial"], true);
    assert_eq!(v["pass"], false, "a partial verdict never passes");
    let status = |id: &str| {
        v["steps"]
            .as_array()
            .unwrap()
            .iter()
            .find(|s| s["id"] == id)
            .unwrap()["status"]
            .clone()
    };
    assert_eq!(status("scan"), "pass", "{v}");
    assert_eq!(status("checkers"), "pass", "{v}");
    assert_eq!(status("unity-editmode"), "skipped");
    assert_eq!(v["artifacts"][0]["sha256"], sha);
    assert_eq!(v["files"].as_array().unwrap().len(), 3);
    // The verdict artifact is stored under its reference and parses back.
    let reference = v["verdictRef"].as_str().unwrap();
    let bytes = l.store.get(reference).unwrap().unwrap();
    let parsed = verdict::parse(&bytes).unwrap();
    assert_eq!(parsed.verdict_ref(), reference);
    assert_eq!(
        std::fs::read(l.root.join(&slot).join("out/verdict.json")).unwrap(),
        bytes
    );
    // Step logs are written and referenced.
    let scan_log = l.root.join(&slot).join("out/logs/scan.log");
    assert!(scan_log.is_file());
    // The slot checker agrees, and the candidate was not left behind.
    let check = std::process::Command::new("python3")
        .arg(repo().join("tools/check_stage_slot.py"))
        .arg(l.root.join(&slot))
        .output()
        .unwrap();
    assert!(
        check.status.success(),
        "{}",
        String::from_utf8_lossy(&check.stdout)
    );
    assert!(
        !l.root
            .join(".incoming")
            .join(answer.body["jobId"].as_str().unwrap())
            .exists()
    );

    // One slot per change set.
    let err = l
        .runner
        .request(json!({"changeSetId": cs, "slot": "other", "steps": ["scan"]}))
        .unwrap_err();
    assert_eq!(err.code(), "ledger_conflict");

    // Discard (Reject) removes the slot.
    let answer = l
        .runner
        .request(json!({"changeSetId": cs, "action": "discard"}))
        .unwrap();
    assert_eq!(answer.body["discarded"], true);
    assert!(!l.root.join(&slot).exists());
}

#[tokio::test(flavor = "multi_thread")]
async fn forbidden_content_fails_the_scan_and_no_code_runs() {
    let l = lane();
    let cs = "cs_01JAPP0000000000000000SAN2";
    put_candidate(&l, cs, FORBIDDEN);
    let answer = l.runner.request(json!({"changeSetId": cs})).unwrap();
    let job = wait_job(&l, answer.body["jobId"].as_str().unwrap()).await;
    let v = &job["verdict"];
    assert_eq!(v["pass"], false);
    let rules: Vec<&str> = v["forbiddenHits"]
        .as_array()
        .unwrap()
        .iter()
        .map(|h| h["rule"].as_str().unwrap())
        .collect();
    assert!(rules.contains(&"process-start"), "{v}");
    assert!(rules.contains(&"static-mutable"), "{v}");
    for id in ["dotnet", "unity-editmode", "playmode-smoke", "determinism"] {
        let step = v["steps"]
            .as_array()
            .unwrap()
            .iter()
            .find(|s| s["id"] == id)
            .unwrap();
        assert_eq!(step["status"], "skipped", "{id}: {step}");
        assert!(
            step["detail"].as_str().unwrap().contains("scan failed"),
            "{step}"
        );
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn bad_lane_requests_are_refused() {
    let l = lane();
    let e = l
        .runner
        .request(json!({"changeSetId": "cs_01JAPP0000000000000000SAN3"}))
        .unwrap_err();
    assert_eq!(e.code(), "not_found");
    let e = l
        .runner
        .request(json!({"changeSetId": "cs_01JAPP0000000000000000SAN3", "steps": ["compile"]}))
        .unwrap_err();
    assert_eq!(e.code(), "bad_request");
    let e = l
        .runner
        .request(json!({"changeSetId": "nope", "action": "discard"}))
        .unwrap_err();
    assert_eq!(e.code(), "bad_request");
    let a = l
        .runner
        .request(json!({"changeSetId": "cs_01JAPP0000000000000000SAN3", "action": "discard"}))
        .unwrap();
    assert_eq!(a.body["discarded"], false);
}
