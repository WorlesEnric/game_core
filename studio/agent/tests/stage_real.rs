#![allow(clippy::unwrap_used, clippy::expect_used)]
//! The real staging lane on the Linux build host (`cargo test -- --ignored stage_real`): the
//! pressure-plate candidate (`samples/mechanisms/pressure-plate/candidate`) through every step,
//! batchmode Unity included, under the host-wide Unity lock. Needs the Unity Editor, dotnet and
//! python3; never runs on the Mac.
//!
//! The first stage on a host imports the whole slot project; it runs once with a 30-minute
//! budget to seed `<root>/_warm/Library`. The measured stage then starts from a fresh slot with
//! the warm Library copied in, under B-STAGE (6 min), and its duration is written to
//! `<root>/stage-real.json` (and printed) for the P2.4 evidence. The two negative fixtures
//! must fail at the scan and at the EditMode step respectively (the latter from the warm Library,
//! under B-STAGE).

use std::path::{Path, PathBuf};
use std::time::Duration;

use gamecore_studio::stage::pipeline::{self, SlotSource, StageOptions};
use gamecore_studio::stage::slot;
use gamecore_studio::stage::verdict::{B_STAGE_MS, StageVerdict, StepStatus};
use serde_json::json;

fn repo() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .unwrap()
        .to_path_buf()
}

fn fixture(name: &str) -> PathBuf {
    repo().join("samples/mechanisms/pressure-plate").join(name)
}

fn stage(slot_id: &str, candidate: &Path, budget: Duration) -> StageVerdict {
    let r = repo();
    let mut opts =
        StageOptions::from_env(&r, slot_id, SlotSource::Candidate(candidate.to_path_buf()));
    opts.budget = budget;
    // A cold slot (no warm Library yet) imports the whole project in its first Editor run; let
    // one attempt use the whole (longer) budget there. Under B-STAGE the runner's own bound holds.
    if budget > Duration::from_millis(B_STAGE_MS) {
        opts.unity_attempt = budget;
    }
    opts.force = true;
    let v = pipeline::run_stage(&opts).unwrap();
    for s in &v.steps {
        eprintln!(
            "   {:<15} {:?} {:>8} ms  {}",
            s.id, s.status, s.duration_ms, s.detail
        );
    }
    v
}

fn status(v: &StageVerdict, id: &str) -> StepStatus {
    v.step(id).unwrap().status
}

/// Seeds `<root>/_warm/Library` with one cold stage of the clean candidate (30-minute budget)
/// unless it exists; returns the warm-up duration when one ran.
fn warm_up() -> Option<u64> {
    let root = slot::default_root();
    if root.join("_warm").join("Library").is_dir() {
        return None;
    }
    let v = stage(
        "warmup-plate",
        &fixture("candidate"),
        Duration::from_secs(1800),
    );
    assert!(
        v.step("unity-editmode").unwrap().status == StepStatus::Pass,
        "warm-up compile failed: {}",
        serde_json::to_string_pretty(&v.to_value()).unwrap()
    );
    slot::remove_slot(&root.join("warmup-plate")).unwrap();
    Some(v.duration_ms)
}

#[test]
#[ignore = "host only: batchmode Unity, dotnet and python3 (cargo test -- --ignored stage_real)"]
fn stage_real_pressure_plate() {
    let root = slot::default_root();
    let warmup_ms = warm_up();
    let slot_id = "stage-real-plate";
    slot::remove_slot(&root.join(slot_id)).unwrap();
    let v = stage(
        slot_id,
        &fixture("candidate"),
        Duration::from_millis(B_STAGE_MS),
    );
    let record = json!({
        "test": "stage_real_pressure_plate",
        "changeSetId": v.change_set_id,
        "slot": v.slot,
        "pass": v.pass,
        "durationMs": v.duration_ms,
        "budgetMs": v.budget_ms,
        "withinBStage": v.duration_ms <= B_STAGE_MS,
        "warmupMs": warmup_ms,
        "verdictRef": v.verdict_ref(),
        "steps": v.steps.iter().map(|s| json!({"id": s.id, "status": s.status, "durationMs": s.duration_ms, "detail": s.detail})).collect::<Vec<_>>(),
        "catalogDelta": v.catalog_delta,
        "host": v.runner.host,
    });
    std::fs::write(
        root.join("stage-real.json"),
        serde_json::to_vec_pretty(&record).unwrap(),
    )
    .unwrap();
    println!("STAGE_REAL {record}");
    assert!(
        v.pass,
        "the pressure plate must stage: {}",
        serde_json::to_string_pretty(&v.to_value()).unwrap()
    );
    assert!(
        v.duration_ms <= B_STAGE_MS,
        "B-STAGE exceeded: {} ms",
        v.duration_ms
    );
    assert!(v.catalog_delta.predicted.is_some());
    assert_eq!(v.catalog_delta.mechanisms.len(), 1);
}

#[test]
#[ignore = "host only: batchmode Unity, dotnet and python3 (cargo test -- --ignored stage_real)"]
fn stage_real_negative_fixtures() {
    let v = stage(
        "stage-real-forbidden",
        &fixture("candidate-forbidden"),
        Duration::from_secs(900),
    );
    assert!(!v.pass);
    assert_eq!(status(&v, "scan"), StepStatus::Fail);
    let rules: Vec<&str> = v.forbidden_hits.iter().map(|h| h.rule.as_str()).collect();
    assert!(rules.contains(&"process-start"), "{rules:?}");
    assert!(rules.contains(&"static-mutable"), "{rules:?}");
    assert_eq!(status(&v, "unity-editmode"), StepStatus::Skipped);

    warm_up();
    let v = stage(
        "stage-real-failing-test",
        &fixture("candidate-failing-test"),
        Duration::from_millis(B_STAGE_MS),
    );
    assert!(!v.pass);
    assert_eq!(status(&v, "scan"), StepStatus::Pass);
    assert_eq!(status(&v, "checkers"), StepStatus::Pass);
    assert_eq!(status(&v, "unity-editmode"), StepStatus::Fail);
    assert!(
        v.step("unity-editmode")
            .unwrap()
            .detail
            .contains("EditMode test"),
        "{:?}",
        v.step("unity-editmode")
    );
    assert_eq!(status(&v, "playmode-smoke"), StepStatus::Skipped);
    for s in ["stage-real-forbidden", "stage-real-failing-test"] {
        slot::remove_slot(&slot::default_root().join(s)).unwrap();
    }
}
