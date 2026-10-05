//! The stage verdict (`gamecore.studio.stage-verdict/1`), the only thing that lets staged code
//! reach the live editor: Studio's `StageAdmission` refuses to apply a mechanism unless a
//! passing verdict whose artifact digests match the change set exists.
//!
//! The verdict's bytes are its canonical JSON (keys sorted, no nulls); its id (`verdictRef`)
//! is the SHA-256 of those bytes. The runner writes them to `<slot>/out/verdict.json` and,
//! for an API job, into the content store (`GET /v1/artifacts/{verdictRef}`).

use serde::{Deserialize, Serialize};
use serde_json::Value;

use super::scan::Hit;
use crate::util::{canonical_json, pruned, sha256_hex};

/// Schema id of a verdict.
pub const VERDICT_SCHEMA: &str = "gamecore.studio.stage-verdict/1";

/// B-STAGE (docs/studio/05): a stage takes at most six minutes.
pub const B_STAGE_MS: u64 = 360_000;

/// The steps of a stage, in order.
pub const STEP_IDS: &[&str] = &[
    "scan",
    "checkers",
    "dotnet",
    "unity-editmode",
    "playmode-smoke",
    "determinism",
    "budget",
];

/// Status of one step.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StepStatus {
    /// The step ran and passed.
    Pass,
    /// The step ran and failed (or could not run).
    Fail,
    /// The step did not run (not requested, not applicable, or an earlier step failed).
    Skipped,
}

/// One step's result.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StepResult {
    /// Step id (one of [`STEP_IDS`]).
    pub id: String,
    /// Outcome.
    pub status: StepStatus,
    /// Wall time.
    pub duration_ms: u64,
    /// SHA-256 of the step's redacted log (`<slot>/out/logs/<id>.log`); absent when no log.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub log_ref: Option<String>,
    /// One line on the outcome (redacted).
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub detail: String,
    /// Step facts (test counts, compile errors, hashes).
    #[serde(default, skip_serializing_if = "Value::is_null")]
    pub facts: Value,
}

impl StepResult {
    /// A result without log or facts.
    pub fn new(
        id: &str,
        status: StepStatus,
        duration_ms: u64,
        detail: impl Into<String>,
    ) -> StepResult {
        StepResult {
            id: id.to_string(),
            status,
            duration_ms,
            log_ref: None,
            detail: detail.into(),
            facts: Value::Null,
        }
    }

    /// Every mandatory step must actually pass; semantic scan is mandatory even without rules.
    pub fn acceptable(&self) -> bool {
        self.status == StepStatus::Pass
    }
}

/// A code artifact the verdict covers (admission compares each digest with the change set).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CoveredArtifact {
    /// `package` (the archive) or `proposal`.
    pub role: String,
    /// Lowercase hex SHA-256.
    pub sha256: String,
}

/// A file of the staged package.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackageFile {
    /// Path relative to the package root.
    pub path: String,
    /// Lowercase hex SHA-256.
    pub sha256: String,
    /// Size.
    #[serde(default)]
    pub bytes: u64,
}

/// One mechanism catalog in a catalog delta.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MechanismCatalog {
    /// Package name.
    pub package: String,
    /// The generated catalog type (full name).
    pub catalog_type: String,
    /// Its catalog fingerprint (hex).
    pub fingerprint: String,
}

/// What admitting the package does to the live catalog: the world's fingerprint, the
/// mechanism catalogs it adds, and the catalog-set hash the live world must report after
/// admission (`predicted`; the formula is in `studio/stage/README.md`).
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CatalogDelta {
    /// The baked world catalog fingerprint the stage inputs produce.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub world: Option<String>,
    /// The mechanism catalogs.
    #[serde(default)]
    pub mechanisms: Vec<MechanismCatalog>,
    /// The catalog-set hash after admission.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub predicted: Option<String>,
}

/// The runner that produced a verdict.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RunnerInfo {
    /// Companion version.
    pub version: String,
    /// Host name (no user data).
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub host: String,
    /// Source repository commit the slot was built from.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub source_commit: Option<String>,
    /// Template digest of the slot.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub template: Option<String>,
}

/// The verdict.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StageVerdict {
    /// Actual OS confinement, always recorded.
    #[serde(default)]
    pub confinement: String,
    /// First use of this Unity/package-version cache.
    #[serde(default)]
    pub cold_cache: bool,
    /// [`VERDICT_SCHEMA`].
    pub schema: String,
    /// The staged change set.
    pub change_set_id: String,
    /// The slot.
    pub slot: String,
    /// The package name.
    pub package: String,
    /// Step results in [`STEP_IDS`] order.
    pub steps: Vec<StepResult>,
    /// True only when every step is acceptable and the run was not partial.
    pub pass: bool,
    /// True when only some steps were requested (a partial verdict never passes).
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub partial: bool,
    /// Code artifacts covered.
    pub artifacts: Vec<CoveredArtifact>,
    /// The package files staged (path + sha256), as admission must find them.
    pub files: Vec<PackageFile>,
    /// The catalog delta (empty when the Unity steps did not run).
    pub catalog_delta: CatalogDelta,
    /// Forbidden-content findings.
    pub forbidden_hits: Vec<Hit>,
    /// The `stage_failed` reason when the stage failed as a whole (`timeout`, `slot`, ...).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub failure: Option<String>,
    /// Runner facts.
    pub runner: RunnerInfo,
    /// Total wall time.
    pub duration_ms: u64,
    /// The budget the stage ran under.
    pub budget_ms: u64,
    /// When the verdict was made (ms since the epoch).
    pub created_at: i64,
}

impl StageVerdict {
    /// Recompute `pass` from the steps.
    pub fn settle(&mut self) {
        let all_present = STEP_IDS
            .iter()
            .all(|id| self.steps.iter().any(|s| s.id == *id));
        self.pass = !self.partial
            && self.failure.is_none()
            && all_present
            && self.steps.len() == STEP_IDS.len()
            && self.forbidden_hits.is_empty()
            && self.steps.iter().all(StepResult::acceptable);
    }

    /// The verdict as a JSON value without nulls.
    pub fn to_value(&self) -> Value {
        pruned(serde_json::to_value(self).unwrap_or(Value::Null))
    }

    /// The verdict's bytes: canonical JSON.
    pub fn bytes(&self) -> Vec<u8> {
        canonical_json(&self.to_value()).into_bytes()
    }

    /// The verdict id: SHA-256 of [`StageVerdict::bytes`].
    pub fn verdict_ref(&self) -> String {
        sha256_hex(&self.bytes())
    }

    /// The step with `id`.
    pub fn step(&self, id: &str) -> Option<&StepResult> {
        self.steps.iter().find(|s| s.id == id)
    }
}

/// Parse verdict bytes, checking the schema.
pub fn parse(bytes: &[u8]) -> Result<StageVerdict, String> {
    let v: StageVerdict =
        serde_json::from_slice(bytes).map_err(|e| format!("not a stage verdict: {e}"))?;
    if v.schema != VERDICT_SCHEMA {
        return Err(format!(
            "verdict schema is {}, expected {VERDICT_SCHEMA}",
            v.schema
        ));
    }
    Ok(v)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn sample() -> StageVerdict {
        let mut steps: Vec<StepResult> = STEP_IDS
            .iter()
            .map(|id| StepResult::new(id, StepStatus::Pass, 10, ""))
            .collect();
        steps[2] = StepResult {
            facts: json!({"notApplicable": true}),
            ..StepResult::new(
                "dotnet",
                StepStatus::Pass,
                0,
                "semantic scan passed; no rules half",
            )
        };
        steps[0].log_ref = Some("a".repeat(64));
        let mut v = StageVerdict {
            confinement: "docker".into(),
            cold_cache: false,
            schema: VERDICT_SCHEMA.into(),
            change_set_id: "cs_01JAPP0000000000000000PXAT".into(),
            slot: "cs-01japp0000000000000000pxat".into(),
            package: "com.hollowmere.mechanism.pressureplate".into(),
            steps,
            pass: false,
            partial: false,
            artifacts: vec![CoveredArtifact {
                role: "package".into(),
                sha256: "b".repeat(64),
            }],
            files: vec![PackageFile {
                path: "package.json".into(),
                sha256: "c".repeat(64),
                bytes: 12,
            }],
            catalog_delta: CatalogDelta {
                world: Some("d".repeat(64)),
                mechanisms: vec![MechanismCatalog {
                    package: "com.hollowmere.mechanism.pressureplate".into(),
                    catalog_type: "Hollowmere.Mechanism.PressurePlate.PressurePlateCatalog".into(),
                    fingerprint: "e".repeat(64),
                }],
                predicted: Some("f".repeat(64)),
            },
            forbidden_hits: vec![],
            failure: None,
            runner: RunnerInfo {
                version: "0.1.0".into(),
                ..RunnerInfo::default()
            },
            duration_ms: 70,
            budget_ms: B_STAGE_MS,
            created_at: 1,
        };
        v.settle();
        v
    }

    #[test]
    fn verdict_serialises_camel_case_without_nulls_and_round_trips() {
        let v = sample();
        assert!(v.pass);
        let value = v.to_value();
        assert_eq!(value["changeSetId"], "cs_01JAPP0000000000000000PXAT");
        assert_eq!(value["steps"][0]["status"], "pass");
        assert_eq!(value["steps"][0]["durationMs"], 10);
        assert_eq!(value["steps"][0]["logRef"], "a".repeat(64));
        assert!(value["steps"][1].get("logRef").is_none());
        assert_eq!(value["steps"][2]["status"], "pass");
        assert_eq!(
            value["catalogDelta"]["mechanisms"][0]["catalogType"],
            "Hollowmere.Mechanism.PressurePlate.PressurePlateCatalog"
        );
        assert!(value.get("failure").is_none());
        assert!(value.get("partial").is_none());
        assert_eq!(value["forbiddenHits"], json!([]));
        let bytes = v.bytes();
        assert!(!String::from_utf8_lossy(&bytes).contains("null"));
        let back = parse(&bytes).unwrap();
        assert_eq!(back, v);
        assert_eq!(back.verdict_ref(), v.verdict_ref());
        assert_eq!(v.verdict_ref(), sha256_hex(&bytes));
    }

    #[test]
    fn the_verdict_ref_is_stable_and_changes_with_content() {
        let a = sample();
        let mut b = sample();
        assert_eq!(a.verdict_ref(), b.verdict_ref());
        b.files[0].sha256 = "0".repeat(64);
        assert_ne!(a.verdict_ref(), b.verdict_ref());
    }

    #[test]
    fn r2_09_not_applicable_never_bypasses_a_mandatory_step() {
        let mut verdict = sample();
        verdict.steps[2].status = StepStatus::Skipped;
        verdict.steps[2].facts = json!({"notApplicable":true});
        verdict.settle();
        assert!(!verdict.pass);
        let mut verdict = sample();
        verdict.steps.push(verdict.steps[0].clone());
        verdict.settle();
        assert!(!verdict.pass);
    }

    #[test]
    fn pass_needs_every_step_acceptable_and_a_full_run() {
        let mut v = sample();
        v.steps[3].status = StepStatus::Fail;
        v.settle();
        assert!(!v.pass);

        let mut v = sample();
        v.steps[4] = StepResult::new("playmode-smoke", StepStatus::Skipped, 0, "not requested");
        v.settle();
        assert!(
            !v.pass,
            "a skipped step that is applicable is not acceptable"
        );

        let mut v = sample();
        v.partial = true;
        v.settle();
        assert!(!v.pass);

        let mut v = sample();
        v.steps.retain(|s| s.id != "budget");
        v.settle();
        assert!(!v.pass);

        let mut v = sample();
        v.forbidden_hits.push(Hit {
            rule: "process-start".into(),
            path: "Runtime/A.cs".into(),
            line: 3,
            excerpt: "Process.Start(x)".into(),
        });
        v.settle();
        assert!(!v.pass);

        let mut v = sample();
        v.failure = Some("timeout".into());
        v.settle();
        assert!(!v.pass);
    }

    #[test]
    fn a_wrong_schema_is_refused() {
        let mut value = sample().to_value();
        value["schema"] = json!("other/1");
        assert!(parse(canonical_json(&value).as_bytes()).is_err());
        assert!(parse(b"{}").is_err());
    }
}
