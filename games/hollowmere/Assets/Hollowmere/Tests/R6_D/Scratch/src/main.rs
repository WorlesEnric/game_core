#![allow(clippy::unwrap_used, clippy::expect_used)]
// The existing integration fixture supplies scratch-node authentication only. All staging,
// compilation, catalog prediction, signing and verification remain production companion code.
#[path = "../../../../../../../../studio/agent/tests/support/mod.rs"]
mod support;

use gamecore_studio::ledger::{CandidateRow, Ledger, NewRequest, RequestUpdate};
use gamecore_studio::model::RequestState;
use gamecore_studio::stage::pipeline::{cache_version, SlotSource, StageOptions};
use gamecore_studio::store::ArtifactStore;
use gamecore_studio::util::sha256_hex;
use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};
use support::{FakeNode, AGENT, AGENT_KEY, APP_KEY};

struct Companion(std::process::Child);
impl Drop for Companion {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

fn save(path: &Path, value: &Value) {
    std::fs::write(path, serde_json::to_vec_pretty(value).unwrap()).unwrap();
}

fn revision(repo: &Path) -> String {
    let output = Command::new("git")
        .args(["rev-parse", "HEAD"])
        .current_dir(repo)
        .output()
        .unwrap();
    assert!(output.status.success(), "cannot determine current revision");
    String::from_utf8(output.stdout).unwrap().trim().to_string()
}

fn record_node_evidence(node: &FakeNode, evidence: &Path, job: &str) {
    let route = format!("/api/v1/agents/{AGENT}/http/v1/stage/{job}");
    let provider_calls = node.lock().op_calls.len();
    save(
        &evidence.join("scratch-node.json"),
        &json!({
            "nodeUrl": node.url,
            "jobId": job,
            "providerCalls": provider_calls,
            "verdictFetches": node.calls("GET", &format!("{route}/verdict")),
            "verdictVerifications": node.calls("POST", &format!("{route}/verify")),
        }),
    );
    assert_eq!(provider_calls, 0, "provider operations are forbidden");
}

#[tokio::main]
async fn main() {
    let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(7)
        .unwrap()
        .canonicalize()
        .unwrap();
    let mut args = std::env::args().skip(1);
    let mut companion_path = None;
    let mut cache_path = None;
    let mut evidence_path = None;
    while let Some(flag) = args.next() {
        let value = PathBuf::from(args.next().expect("each option requires a path"));
        match flag.as_str() {
            "--companion" => companion_path = Some(value),
            "--cache" => cache_path = Some(value),
            "--evidence" => evidence_path = Some(value),
            _ => panic!(
                "unknown option {flag}; expected --companion PATH --cache PATH --evidence PATH"
            ),
        }
    }
    let companion_path = companion_path
        .expect("--companion requires the freshly built gamecore-studio binary")
        .canonicalize()
        .unwrap();
    let cache_source = cache_path
        .expect("--cache requires the exact provisioned versioned cache")
        .canonicalize()
        .unwrap();
    let evidence =
        evidence_path.expect("--evidence requires a fresh directory below .evidence/r6-d");
    let evidence = if evidence.is_absolute() {
        evidence
    } else {
        repo.join(evidence)
    };
    let evidence_root = repo.join(".evidence/r6-d");
    std::fs::create_dir_all(&evidence_root).unwrap();
    assert!(
        !evidence.exists(),
        "refusing reused evidence; choose a new run directory"
    );
    let parent = evidence.parent().unwrap().canonicalize().unwrap();
    assert!(
        parent.starts_with(evidence_root.canonicalize().unwrap()),
        "scratch evidence must stay below .evidence/r6-d"
    );
    std::fs::create_dir(&evidence).unwrap();
    let evidence = evidence.canonicalize().unwrap();
    let state = evidence.join("service");
    std::fs::create_dir(&state).unwrap();
    let revision = revision(&repo);
    let node = FakeNode::start().await;
    let project = "1".repeat(64);
    let catalog = "2".repeat(64);
    let owner = json!(["gamecore-unity", project]).to_string();
    let candidate = repo.join("samples/mechanisms/pressure-plate/candidate");
    let change_set: Value =
        serde_json::from_slice(&std::fs::read(candidate.join("change-set.json")).unwrap()).unwrap();
    let cs = change_set["id"].as_str().unwrap().to_string();

    // Fixture candidate bytes, not a generated/provider request. The subsequent Docker stage
    // independently scans, compiles, runs checkers and executes EditMode/PlayMode tests.
    let ledger = Ledger::open(&state.join("ledger.db")).unwrap();
    ledger
        .insert_request(&NewRequest {
            change_set_id: cs.clone(),
            digest: "stage-int-fixture".into(),
            body: json!({"toolCatalogRevision": catalog}),
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
    let opts = StageOptions::from_env(&repo, "fixture", SlotSource::Existing);
    let version = cache_version(&opts).unwrap();
    assert_eq!(
        cache_source.file_name().unwrap().to_str().unwrap(),
        version,
        "cache is not for this revision's pinned inputs"
    );
    assert!(
        Command::new(repo.join("studio/stage/provision-cache.sh"))
            .arg(&cache_source)
            .arg("--verify")
            .status()
            .unwrap()
            .success(),
        "operator cache did not verify"
    );
    let cache = stage_root
        .join(sha256_hex(owner.as_bytes()))
        .join("_warm")
        .join(version);
    std::fs::create_dir_all(cache.parent().unwrap()).unwrap();
    assert!(
        Command::new("cp")
            .args(["-a", "--reflink=auto"])
            .arg(&cache_source)
            .arg(&cache)
            .status()
            .unwrap()
            .success(),
        "private cache copy failed"
    );
    std::fs::write(
        state.join("config.toml"),
        format!(
            "follow_wait_ms = 200\n[stage.projects]\n\"{project}\" = {}\n",
            serde_json::to_string(&repo.join("games/hollowmere").to_string_lossy()).unwrap()
        ),
    )
    .unwrap();
    // This file is a public synthetic fixture string written here; no installed key is read.
    let credential = state.join("synthetic-node-token");
    std::fs::write(&credential, AGENT_KEY).unwrap();
    let executable = state.join("gamecore-studio");
    std::fs::copy(companion_path, &executable).unwrap();
    let log = std::fs::File::create(state.join("companion.log")).unwrap();
    let mut child = Companion(
        Command::new(&executable)
            .env("ETOS_URL", &node.url)
            .env("ETOS_KEY_FILE", &credential)
            .env("ETOS_AGENT", AGENT)
            .env("ETOS_STATE_DIR", &state)
            .env("GAMECORE_STAGE_ROOT", &stage_root)
            .env("GAMECORE_STAGE_REPO", &repo)
            .env("GAMECORE_STAGE_UNITY_TIMEOUT_S", "1500")
            .stdout(Stdio::null())
            .stderr(log)
            .spawn()
            .unwrap(),
    );
    let startup = Instant::now();
    loop {
        assert!(
            startup.elapsed() < Duration::from_secs(30),
            "scratch companion startup deadline"
        );
        assert!(
            child.0.try_wait().unwrap().is_none(),
            "scratch companion exited; inspect service/companion.log"
        );
        if node.lock().endpoint.is_some() {
            break;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    // Exercise the same authenticated app proxy as the Unity client, not a direct bypass.
    let http = reqwest::Client::new();
    let request = |method, route: &str| {
        http.request(
            method,
            format!("{}/api/v1/agents/{AGENT}/http{route}", node.url),
        )
        .bearer_auth(APP_KEY)
        .header("X-GameCore-Project", &project)
    };
    let response = request(reqwest::Method::POST, "/v1/stage")
        .json(&json!({
            "changeSetId": cs, "projectId": project,
            "sourceRevision": revision, "catalogRevision": catalog,
        }))
        .send()
        .await
        .unwrap();
    assert_eq!(response.status(), 202, "fresh stage was not queued");
    let queued: Value = response.json().await.unwrap();
    save(&evidence.join("queued-job.json"), &queued);
    let job = queued["jobId"].as_str().unwrap();
    let route = format!("/v1/stage/{job}");
    let started = Instant::now();
    let completed = loop {
        let status: Value = request(reqwest::Method::GET, &route)
            .send()
            .await
            .unwrap()
            .error_for_status()
            .unwrap()
            .json()
            .await
            .unwrap();
        if status["state"] == "done" || status["state"] == "failed" {
            break status;
        }
        assert!(
            started.elapsed() < Duration::from_secs(2100),
            "stage service deadline"
        );
        tokio::time::sleep(Duration::from_secs(2)).await;
    };
    save(&evidence.join("service-job.json"), &completed);
    if completed["verdict"]["pass"] != true {
        let issued = request(reqwest::Method::GET, &format!("{route}/verdict"))
            .send()
            .await
            .unwrap();
        save(
            &evidence.join("issuance.json"),
            &json!({"jobId": job, "issued": false, "httpStatus": issued.status().as_u16()}),
        );
        assert_eq!(
            issued.status(),
            404,
            "a failing stage must not issue authority"
        );
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
    assert_eq!(signed["sourceRevision"], revision);
    assert_eq!(signed["steps"].as_array().unwrap().len(), 7);
    let verified: Value = request(reqwest::Method::POST, &format!("{route}/verify"))
        .json(&signed)
        .send()
        .await
        .unwrap()
        .error_for_status()
        .unwrap()
        .json()
        .await
        .unwrap();
    assert_eq!(verified["verified"], true);
    save(&evidence.join("signed-verdict.json"), &signed);
    save(&evidence.join("service-verify.json"), &verified);
    let slot = stage_root
        .join(sha256_hex(owner.as_bytes()))
        .join(completed["slot"].as_str().unwrap());
    for file in ["semantic-findings.json", "editmode.xml", "playmode.xml"] {
        std::fs::copy(slot.join("out").join(file), evidence.join(file)).unwrap();
    }
    for mode in ["graphical", "batch"] {
        let live_evidence = evidence.join(mode);
        std::fs::create_dir(&live_evidence).unwrap();
        save(
            &live_evidence.join("live-config.json"),
            &json!({
                "nodeUrl": node.url, "jobId": job, "evidence": live_evidence, "candidate": candidate,
                "request": {"changeSetId": cs, "projectId": project,
                    "sourceProject": repo.join("games/hollowmere"), "sourceRevision": revision,
                    "catalogRevision": catalog, "packageDigest": signed["packageDigest"],
                    "proposalDigest": signed["proposalDigest"], "stageInputs": []},
            }),
        );
    }
    save(
        &evidence.join("ready.json"),
        &json!({
            "jobId": job, "sourceRevision": revision, "nodeUrl": node.url,
            "graphicalConfig": evidence.join("graphical/live-config.json"),
            "batchConfig": evidence.join("batch/live-config.json"),
        }),
    );
    println!("R6_D_READY {}", evidence.display());
    println!("Run graphical then batch with R6_D/run-live.py and the corresponding live-config.json; keep this process alive until both finish.");
    record_node_evidence(&node, &evidence, job);

    // Main supervises this service through hub. Both sequential Editor processes re-fetch and
    // verify the same fresh signed job. TERM/Ctrl-C only ends this scratch process and its child.
    let mut terminate =
        tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate()).unwrap();
    let mut audit = tokio::time::interval(Duration::from_secs(2));
    loop {
        tokio::select! {
            _ = tokio::signal::ctrl_c() => break,
            _ = terminate.recv() => break,
            _ = audit.tick() => {
                assert!(child.0.try_wait().unwrap().is_none(), "scratch companion exited");
                record_node_evidence(&node, &evidence, job);
            }
        }
    }
    record_node_evidence(&node, &evidence, job);
}
