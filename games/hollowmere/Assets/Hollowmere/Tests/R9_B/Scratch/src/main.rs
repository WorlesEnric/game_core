#![allow(clippy::unwrap_used, clippy::expect_used)]
// Reuse only the authentication/proxy fixture. The stage service, signing authority,
// Docker compilation/tests and verdict verification are the real companion from this tree.
#[path = "../../../../../../../../studio/agent/tests/support/mod.rs"]
mod support;

use gamecore_studio::stage::pipeline::{SlotSource, StageOptions, cache_version};
use gamecore_studio::util::sha256_hex;
use serde_json::{Value, json};
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};
use support::{AGENT, AGENT_KEY, APP_KEY, FakeNode};

struct Companion(std::process::Child);
impl Drop for Companion {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

fn save(path: &Path, value: &Value) {
    let temporary = path.with_extension("tmp");
    std::fs::write(&temporary, serde_json::to_vec_pretty(value).unwrap()).unwrap();
    std::fs::rename(temporary, path).unwrap();
}

fn revision(repo: &Path) -> String {
    let output = Command::new("git")
        .args(["rev-parse", "HEAD"])
        .current_dir(repo)
        .output()
        .unwrap();
    assert!(output.status.success(), "cannot determine source revision");
    String::from_utf8(output.stdout).unwrap().trim().to_string()
}

fn audit(node: &FakeNode, evidence: &Path, job: &str) -> Value {
    let inner = node.lock();
    let provider_calls = inner
        .op_calls
        .iter()
        .filter(|(name, _)| name != "status")
        .count();
    let path = format!("/agents/{AGENT}/http/v1/stage/{job}");
    let count = |method: &str, suffix: &str| {
        let route = format!("{path}/{suffix}");
        inner
            .log
            .iter()
            .filter(|(m, p, _)| m == method && p == &route)
            .count()
    };
    let result = json!({
        "nodeUrl": node.url, "jobId": job, "providerCalls": provider_calls,
        "taskCount": inner.tasks.len(), "verdictFetches": count("GET", "verdict"),
        "verdictVerifications": count("POST", "verify"),
    });
    assert_eq!(provider_calls, 0, "provider operations are forbidden");
    assert!(inner.tasks.is_empty(), "worker tasks are forbidden");
    save(&evidence.join("scratch-node.json"), &result);
    result
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
    let (mut binary, mut cache, mut context, mut evidence, mut service_root) =
        (None, None, None, None, None);
    while let Some(flag) = args.next() {
        let value = PathBuf::from(args.next().expect("each option requires a path"));
        match flag.as_str() {
            "--companion" => binary = Some(value),
            "--cache" => cache = Some(value),
            "--context" => context = Some(value),
            "--evidence" => evidence = Some(value),
            "--state-root" => service_root = Some(value),
            _ => panic!(
                "expected --companion PATH --cache PATH --context PATH --evidence PATH [--state-root PATH]"
            ),
        }
    }
    let binary = binary
        .expect("--companion must name the freshly built binary")
        .canonicalize()
        .unwrap();
    let cache_source = cache
        .expect("--cache must name the exact versioned cache")
        .canonicalize()
        .unwrap();
    let context: Value = serde_json::from_slice(
        &std::fs::read(context.expect("--context requires run-live.py prepare output")).unwrap(),
    )
    .unwrap();
    let source_project = repo.join("games/hollowmere");
    assert_eq!(
        context["sourceProject"].as_str().unwrap(),
        source_project.to_str().unwrap()
    );
    let revision = revision(&repo);
    assert_eq!(
        context["sourceRevision"], revision,
        "prepare again after source revision changes"
    );
    let project = context["projectId"].as_str().unwrap();
    let catalog = context["catalogRevision"].as_str().unwrap();
    assert_eq!(project.len(), 64);
    assert_eq!(catalog.len(), 64);
    assert!(
        !source_project
            .join("UserSettings/GameCoreStudio.json")
            .exists(),
        "project ETOS settings must be absent; never inspect or overwrite them"
    );
    let evidence = evidence.expect(
        "--evidence requires a fresh directory below artifacts/studio/verification/W-MECH-01/r9-b",
    );
    let evidence = if evidence.is_absolute() {
        evidence
    } else {
        repo.join(evidence)
    };
    let evidence_root = repo.join("artifacts/studio/verification/W-MECH-01/r9-b");
    std::fs::create_dir_all(&evidence_root).unwrap();
    assert!(!evidence.exists(), "refusing reused evidence");
    assert!(
        evidence
            .parent()
            .unwrap()
            .canonicalize()
            .unwrap()
            .starts_with(evidence_root.canonicalize().unwrap())
    );
    std::fs::create_dir(&evidence).unwrap();
    let evidence = evidence.canonicalize().unwrap();
    save(&evidence.join("project-context.json"), &context);

    // The disposable installation (credentials, HMAC key, ledger, cache, executable) is
    // outside the checkout. No installed node paths or credentials are inspected.
    let service_root = service_root.unwrap_or_else(|| std::env::temp_dir().join("r9-b-service"));
    std::fs::create_dir_all(&service_root).unwrap();
    let service_root = service_root.canonicalize().unwrap();
    assert!(
        !service_root.starts_with(&repo),
        "signing authority must be external to the checkout"
    );
    let state = tempfile::Builder::new()
        .prefix("node-")
        .tempdir_in(service_root)
        .unwrap()
        .keep();
    std::fs::set_permissions(&state, std::fs::Permissions::from_mode(0o700)).unwrap();
    save(
        &evidence.join("installation.json"),
        &json!({
            "installationState": state, "sourceRevision": revision,
            "companionSha256": sha256_hex(&std::fs::read(&binary).unwrap()),
        }),
    );
    let node = FakeNode::start().await;
    {
        let mut inner = node.lock();
        inner.ops.clear();
        inner.ops.insert(
            "status".into(),
            (
                200,
                json!({"providers": {}, "absent": ["image", "describe", "tts", "3d", "voice"]}),
            ),
        );
    }
    let owner = json!(["gamecore-unity", project]).to_string();
    let fixture = repo.join("samples/mechanisms/pressure-plate/candidate");
    let candidate = evidence.join("candidate");
    assert!(
        Command::new("cp")
            .arg("-a")
            .arg(&fixture)
            .arg(&candidate)
            .status()
            .unwrap()
            .success()
    );
    let mut change_set: Value =
        serde_json::from_slice(&std::fs::read(candidate.join("change-set.json")).unwrap()).unwrap();
    // Fresh envelope identity only: package, proposal, operations and artifact bytes stay exact.
    change_set["id"] = context["changeSetId"].clone();
    save(&candidate.join("change-set.json"), &change_set);
    let cs = change_set["id"].as_str().unwrap();
    let stage_root = state.join("slots");
    let version =
        cache_version(&StageOptions::from_env(&repo, "r6-e", SlotSource::Existing)).unwrap();
    assert_eq!(
        cache_source.file_name().unwrap().to_str().unwrap(),
        version,
        "cache version mismatch"
    );
    assert!(
        Command::new(repo.join("studio/stage/provision-cache.sh"))
            .arg(&cache_source)
            .arg("--verify")
            .status()
            .unwrap()
            .success(),
        "cache verification failed"
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
            serde_json::to_string(&source_project.to_string_lossy()).unwrap(),
        ),
    )
    .unwrap();
    // Only newly created public fixture credentials are written. Runtime authentication
    // reads them through the normal companion and Unity resolvers, never through this harness.
    let agent_file = state.join("synthetic-node-token");
    std::fs::write(&agent_file, AGENT_KEY).unwrap();
    std::fs::set_permissions(&agent_file, std::fs::Permissions::from_mode(0o600)).unwrap();
    let pairing_file = state.join("scratch-app.json");
    save(&pairing_file, &json!({"url": node.url, "key": APP_KEY}));
    std::fs::set_permissions(&pairing_file, std::fs::Permissions::from_mode(0o600)).unwrap();
    let executable = state.join("gamecore-studio");
    std::fs::copy(&binary, &executable).unwrap();
    let log = std::fs::File::create(state.join("companion.log")).unwrap();
    let mut child = Companion(
        Command::new(&executable)
            .env("ETOS_URL", &node.url)
            .env("ETOS_KEY_FILE", &agent_file)
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
    while node.lock().endpoint.is_none() {
        assert!(
            startup.elapsed() < Duration::from_secs(30),
            "scratch startup deadline"
        );
        assert!(
            child.0.try_wait().unwrap().is_none(),
            "scratch companion exited"
        );
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    let http = reqwest::Client::new();
    let request = |method, route: &str| {
        http.request(
            method,
            format!("{}/api/v1/agents/{AGENT}/http{route}", node.url),
        )
        .bearer_auth(APP_KEY)
        .header("X-GameCore-Project", project)
    };
    save(
        &evidence.join("panel-config.json"),
        &json!({
            "nodeUrl": node.url, "pairingFile": pairing_file, "candidate": candidate,
            "evidence": evidence, "context": context
        }),
    );
    println!("R9_B_READY {}", evidence.display());
    let started = Instant::now();
    let binding_path = evidence.join("panel-binding.json");
    while !binding_path.exists() {
        assert!(
            started.elapsed() < Duration::from_secs(2100),
            "panel submission deadline"
        );
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
    let queued: Value = serde_json::from_slice(&std::fs::read(binding_path).unwrap()).unwrap();
    let job = queued["jobId"].as_str().unwrap();
    let route = format!("/v1/stage/{job}");
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
    save(
        &evidence.join("stage-wall.json"),
        &json!({"milliseconds": started.elapsed().as_millis()}),
    );
    if completed["verdict"]["pass"] != true {
        let refused = request(reqwest::Method::GET, &format!("{route}/verdict"))
            .send()
            .await
            .unwrap();
        save(
            &evidence.join("issuance.json"),
            &json!({"jobId": job, "issued": false, "httpStatus": refused.status().as_u16()}),
        );
        assert_eq!(
            refused.status(),
            404,
            "failing stage must not issue authority"
        );
        panic!("stage did not pass; retained service-job.json records the failure");
    }
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
    assert_eq!(signed["projectId"], project);
    assert_eq!(signed["sourceRevision"], revision);
    assert_eq!(signed["catalogRevision"], catalog);
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
    let unauthenticated = http
        .get(format!(
            "{}/api/v1/agents/{AGENT}/http{route}/verdict",
            node.url
        ))
        .send()
        .await
        .unwrap();
    assert_eq!(unauthenticated.status(), 401);
    save(
        &evidence.join("unauthenticated.json"),
        &json!({"httpStatus": 401}),
    );
    let slot = stage_root
        .join(sha256_hex(owner.as_bytes()))
        .join(completed["slot"].as_str().unwrap());
    for file in ["semantic-findings.json", "editmode.xml", "playmode.xml"] {
        std::fs::copy(slot.join("out").join(file), evidence.join(file)).unwrap();
    }
    let graphical = evidence.join("graphical");
    std::fs::create_dir(&graphical).unwrap();
    save(
        &graphical.join("live-config.json"),
        &json!({
            "nodeUrl": node.url, "pairingFile": pairing_file, "jobId": job, "evidence": graphical, "candidate": candidate,
            "request": {"changeSetId": cs, "projectId": project, "sourceProject": source_project,
                "sourceRevision": revision, "catalogRevision": catalog, "packageDigest": signed["packageDigest"],
                "proposalDigest": signed["proposalDigest"], "stageInputs": []},
        }),
    );
    let before_live = audit(&node, &evidence, job);
    save(
        &evidence.join("ready.json"),
        &json!({
            "jobId": job, "sourceRevision": revision, "nodeUrl": node.url,
            "graphicalConfig": graphical.join("live-config.json"), "installationState": state,
            "beforeLive": before_live, "companionSha256": sha256_hex(&std::fs::read(&binary).unwrap()),
        }),
    );
    println!("R9_B_STAGE_PASSED {}", evidence.display());
    // Stage Editors have all exited before READY; only now may the one graphical Editor
    // be launched. Keep this isolated service alive through authenticated reload and undo.
    let mut terminate =
        tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate()).unwrap();
    let mut interval = tokio::time::interval(Duration::from_secs(1));
    loop {
        tokio::select! {
            _ = tokio::signal::ctrl_c() => break,
            _ = terminate.recv() => break,
            _ = interval.tick() => {
                assert!(child.0.try_wait().unwrap().is_none(), "scratch companion exited");
                audit(&node, &evidence, job);
            }
        }
    }
    audit(&node, &evidence, job);
}
