#![allow(clippy::unwrap_used, clippy::expect_used)]
//! Real companion process and Docker stage; the scratch node supplies authentication only.
//! Candidate bytes are seeded as a validated ledger fixture, without worker/provider calls.
mod support;

use gamecore_studio::ledger::{CandidateRow, Ledger, NewRequest, RequestUpdate};
use gamecore_studio::model::RequestState;
use gamecore_studio::stage::pipeline::{SlotSource, StageOptions, cache_version};
use gamecore_studio::store::ArtifactStore;
use gamecore_studio::util::sha256_hex;
use serde_json::{Value, json};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;
use support::{AGENT, AGENT_KEY, FakeNode};

struct Companion(std::process::Child);
impl Drop for Companion {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "myubuntu: provision stage-int cache first; real Docker Unity via shared allocator"]
async fn r2_11_stage_int_docker_pressure_signed_verdict() {
    let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .unwrap();
    let base =
        PathBuf::from(std::env::var_os("HOME").unwrap()).join(".cache/gamecore-studio/stage-int");
    let root = base.join("service");
    std::fs::create_dir_all(&root).unwrap();
    let state = tempfile::Builder::new()
        .prefix("node-")
        .tempdir_in(&root)
        .unwrap()
        .keep();
    let node = FakeNode::start().await;
    let project = "1".repeat(64);
    let catalog = "2".repeat(64);
    let owner = json!(["gamecore-unity", project]).to_string();
    let candidate = repo.join("samples/mechanisms/pressure-plate/candidate");
    let change_set: Value =
        serde_json::from_slice(&std::fs::read(candidate.join("change-set.json")).unwrap()).unwrap();
    let cs = change_set["id"].as_str().unwrap().to_string();
    let ledger = Ledger::open(&state.join("ledger.db")).unwrap();
    ledger
        .insert_request(&NewRequest {
            change_set_id: cs.clone(),
            digest: "stage-int-fixture".into(),
            body: json!({"toolCatalogRevision":catalog}),
            app: owner.clone(),
            worker: "gc-mechanic".into(),
            etos_request_id: "fixture".into(),
            topic: "fixture".into(),
        })
        .unwrap();
    ledger
        .update_request(
            &cs,
            &RequestUpdate {
                state: Some(RequestState::Candidate),
                ..RequestUpdate::default()
            },
        )
        .unwrap();
    let store = ArtifactStore::new(state.join("artifacts"));
    for artifact in change_set["artifacts"].as_array().unwrap() {
        let bytes = std::fs::read(
            candidate
                .join("artifacts")
                .join(artifact["name"].as_str().unwrap()),
        )
        .unwrap();
        let (hash, _) = store.put(&bytes, None).unwrap();
        assert_eq!(hash, artifact["sha256"]);
    }
    ledger
        .put_candidate(&CandidateRow {
            change_set_id: cs.clone(),
            request_id: cs.clone(),
            task_id: "fixture".into(),
            attempt: 1,
            change_set: change_set.clone(),
            artifacts: change_set["artifacts"].clone(),
            diagnostics: json!([]),
            received_at: 1,
        })
        .unwrap();
    drop(ledger);
    let stage_root = state.join("slots");
    let opts = StageOptions::from_env(repo, "fixture", SlotSource::Existing);
    let version = cache_version(&opts).unwrap();
    let cache = stage_root
        .join(sha256_hex(owner.as_bytes()))
        .join("_warm")
        .join(&version);
    std::fs::create_dir_all(cache.parent().unwrap()).unwrap();
    // A private copy of the operator-provisioned cache; no sibling clone or installed state.
    assert!(
        Command::new("cp")
            .args(["-a", "--reflink=auto"])
            .arg(base.join("stage/_warm").join(version))
            .arg(&cache)
            .status()
            .unwrap()
            .success()
    );
    // A newly provisioned owner cache has not consumed its one cold import allowance.
    let _ = std::fs::remove_file(cache.join(".cold-grace-used"));
    std::fs::write(
        state.join("config.toml"),
        format!(
            "follow_wait_ms = 200\n[stage.projects]\n\"{project}\" = \"{}/games/hollowmere\"\n",
            repo.display()
        ),
    )
    .unwrap();
    // Synthetic test credential only. Never reads any installed key or credential file.
    let credential = state.join("synthetic-node-token");
    std::fs::write(&credential, AGENT_KEY).unwrap();
    let log = std::fs::File::create(state.join("companion.log")).unwrap();
    let executable = state.join("gamecore-studio");
    std::fs::copy(env!("CARGO_BIN_EXE_gamecore-studio"), &executable).unwrap();
    let _child = Companion(
        Command::new(&executable)
            .env("ETOS_URL", &node.url)
            .env("ETOS_KEY_FILE", &credential)
            .env("ETOS_AGENT", AGENT)
            .env("ETOS_STATE_DIR", &state)
            .env("GAMECORE_STAGE_ROOT", &stage_root)
            .env("GAMECORE_STAGE_REPO", repo)
            .env("GAMECORE_STAGE_UNITY_TIMEOUT_S", "1500")
            .stdout(Stdio::null())
            .stderr(log)
            .spawn()
            .unwrap(),
    );
    let startup = std::time::Instant::now();
    let endpoint = loop {
        assert!(
            startup.elapsed() < Duration::from_secs(30),
            "scratch companion did not start"
        );
        if let Some(endpoint) = node.inner.lock().unwrap().endpoint.clone() {
            break endpoint;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    };
    let http = reqwest::Client::new();
    let request = |method, route: &str| {
        http.request(method, format!("{endpoint}{route}"))
            .header("X-Etos-App", "gamecore-unity")
            .header("X-Etos-Proxy-Token", node.token())
            .header("X-GameCore-Project", &project)
    };
    let revision = Command::new("git")
        .args(["rev-parse", "HEAD"])
        .current_dir(repo)
        .output()
        .unwrap();
    let response = request(reqwest::Method::POST, "/v1/stage").json(&json!({
        "changeSetId":cs, "projectId":project,
        "sourceRevision":String::from_utf8_lossy(&revision.stdout).trim(), "catalogRevision":catalog
    })).send().await.unwrap();
    assert_eq!(response.status(), 202);
    let queued: Value = response.json().await.unwrap();
    let job = queued["jobId"].as_str().unwrap();
    let route = format!("/v1/stage/{job}");
    let started = std::time::Instant::now();
    let completed = loop {
        let job: Value = request(reqwest::Method::GET, &route)
            .send()
            .await
            .unwrap()
            .json()
            .await
            .unwrap();
        if job["state"] == "done" || job["state"] == "failed" {
            break job;
        }
        assert!(
            started.elapsed() < Duration::from_secs(2100),
            "stage deadline"
        );
        tokio::time::sleep(Duration::from_secs(2)).await;
    };
    let evidence = repo.join("studio/agent/evidence/stage-int");
    std::fs::write(
        evidence.join("service-job.json"),
        serde_json::to_vec_pretty(&completed).unwrap(),
    )
    .unwrap();
    if completed["verdict"]["pass"] != true {
        let issued = request(reqwest::Method::GET, &format!("{route}/verdict"))
            .send()
            .await
            .unwrap();
        assert_eq!(
            issued.status(),
            404,
            "a failing stage must not issue authority"
        );
        std::fs::write(
            evidence.join("issuance.json"),
            serde_json::to_vec_pretty(&json!({"jobId":job,"issued":false,"httpStatus":404}))
                .unwrap(),
        )
        .unwrap();
    }
    assert_eq!(
        completed["verdict"]["pass"], true,
        "{}",
        completed["verdict"]
    );
    let signed: Value = request(reqwest::Method::GET, &format!("{route}/verdict"))
        .send()
        .await
        .unwrap()
        .error_for_status()
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(signed["confinement"], "docker");
    assert_eq!(signed["budgetMs"], 360000);
    assert_eq!(signed["jobId"], job);
    assert_eq!(signed["steps"].as_array().unwrap().len(), 7);
    let verified: Value = request(reqwest::Method::POST, &format!("{route}/verify"))
        .json(&signed)
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(verified["verified"], true);
    std::fs::write(
        evidence.join("signed-verdict.json"),
        serde_json::to_vec_pretty(&signed).unwrap(),
    )
    .unwrap();
    let slot = stage_root
        .join(sha256_hex(owner.as_bytes()))
        .join(completed["slot"].as_str().unwrap());
    for file in ["semantic-findings.json", "editmode.xml", "playmode.xml"] {
        std::fs::copy(slot.join("out").join(file), evidence.join(file)).unwrap();
    }
    println!(
        "Docker stage signed and authenticated: {}; scratch {}",
        job,
        state.display()
    );
    assert!(node.inner.lock().unwrap().op_calls.is_empty());
}
