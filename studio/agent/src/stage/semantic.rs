//! Trusted analyzer context and the strict, shared analyzer wire contract.
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use super::StageOptions;

#[derive(Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
struct Policy {
    mode: String,
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
pub(super) struct Request {
    schema: String,
    references: Vec<PathBuf>,
    support_sources: Vec<PathBuf>,
    policy: Policy,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
struct Finding {
    rule: String,
    file: String,
    line: u32,
    message: String,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
struct Findings {
    schema: String,
    pass: bool,
    findings: Vec<Finding>,
}

pub(super) fn passing(bytes: &[u8]) -> bool {
    serde_json::from_slice::<Findings>(bytes).is_ok_and(|result| {
        result.schema == "gamecore.stage.findings/1"
            && result.findings.iter().all(|f| {
                !f.rule.is_empty() && !f.file.is_empty() && f.line > 0 && !f.message.is_empty()
            })
            && result.pass
            && result.findings.is_empty()
    })
}

fn files(root: &Path, paths: &mut Vec<PathBuf>) -> Result<(), String> {
    if std::fs::symlink_metadata(root)
        .map_err(|e| e.to_string())?
        .file_type()
        .is_symlink()
    {
        return Err("analysis context contains a link".into());
    }
    for entry in std::fs::read_dir(root).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let kind = entry.file_type().map_err(|e| e.to_string())?;
        if kind.is_symlink() {
            return Err("analysis context contains a link".into());
        }
        if kind.is_dir() {
            files(&entry.path(), paths)?;
        } else if kind.is_file() {
            paths.push(entry.path());
        }
    }
    Ok(())
}

pub(super) fn request(opts: &StageOptions) -> Result<Request, String> {
    let project = opts.slot_dir().join("project");
    let mut engine = Vec::new();
    files(
        &opts.sandbox.editor.join("Data/Managed/UnityEngine"),
        &mut engine,
    )?;
    let unity_dll = |path: &PathBuf| {
        path.extension().is_some_and(|ext| ext == "dll")
            && path.file_name().is_some_and(|name| {
                let name = name.to_string_lossy();
                name.starts_with("UnityEngine") || name.starts_with("UnityEditor")
            })
    };
    let mut references: Vec<_> = engine.into_iter().filter(unity_dll).collect();
    if references.is_empty() {
        return Err("trusted Unity engine metadata unavailable".into());
    }
    // Names and digests are operator-provisioned, never discovered from a candidate's DLLs.
    let trusted: std::collections::BTreeMap<String, String> = serde_json::from_str(include_str!(
        "../../../stage/cache/unity-metadata-lock.json"
    ))
    .map_err(|e| e.to_string())?;
    for (relative, digest) in trusted {
        let path = project.join("Library").join(relative);
        let bytes =
            std::fs::read(&path).map_err(|e| format!("trusted Unity metadata missing: {e}"))?;
        if crate::util::sha256_hex(&bytes) != digest {
            return Err("trusted Unity metadata digest mismatch".into());
        }
        references.push(path);
    }
    references.sort();
    let request = Request {
        schema: "gamecore.stage.analyze/1".into(),
        references,
        support_sources: vec![opts.sandbox.packages.clone()],
        policy: Policy { mode: "D1".into() },
    };
    Ok(request)
}

/// Refuse incompatible trusted package closures before launching Unity or asking UPM
/// to resolve a package which the stage manifest intentionally excludes.
pub(super) fn check_dependency_boundary(opts: &StageOptions) -> Result<(), String> {
    let read = |path: &Path| -> Result<serde_json::Value, String> {
        serde_json::from_slice(&std::fs::read(path).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())
    };
    let allow = read(&opts.repo.join("studio/stage/allowlist.json"))?;
    let manifest = read(&opts.slot_dir().join("project/Packages/manifest.json"))?;
    let deps = manifest["dependencies"]
        .as_object()
        .ok_or("stage manifest dependencies missing")?;
    for name in deps.keys().filter(|name| name.starts_with("com.gamecore.")) {
        let meta = read(&opts.sandbox.packages.join(name).join("package.json"))?;
        if let Some(dependencies) = meta["dependencies"].as_object() {
            for dependency in dependencies.keys() {
                let denied = allow["deniedPackages"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .any(|v| v.as_str() == Some(dependency))
                    || allow["deniedGamecorePrefixes"]
                        .as_array()
                        .into_iter()
                        .flatten()
                        .filter_map(|v| v.as_str())
                        .any(|prefix| dependency.starts_with(prefix));
                if denied {
                    return Err(format!(
                        "stage_dependency_denied: {name} requires excluded {dependency}; separate the trusted Studio integration from the stage-compatible gameplay package"
                    ));
                }
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn r2_11_stage_int_denied_transitive_dependency_has_named_seam() {
        let temp = tempfile::tempdir().unwrap();
        let mut opts =
            StageOptions::from_env(temp.path(), "fixture", super::super::SlotSource::Existing);
        opts.root = temp.path().join("slots");
        std::fs::create_dir_all(temp.path().join("studio/stage")).unwrap();
        std::fs::write(
            temp.path().join("studio/stage/allowlist.json"),
            r#"{"deniedPackages":[],"deniedGamecorePrefixes":["com.gamecore.studio."]}"#,
        )
        .unwrap();
        let project = opts.slot_dir().join("project/Packages");
        std::fs::create_dir_all(&project).unwrap();
        std::fs::write(
            project.join("manifest.json"),
            r#"{"dependencies":{"com.gamecore.gameplay.world":"file:/trusted"}}"#,
        )
        .unwrap();
        let package = opts.sandbox.packages.join("com.gamecore.gameplay.world");
        std::fs::create_dir_all(&package).unwrap();
        std::fs::write(
            package.join("package.json"),
            r#"{"dependencies":{"com.gamecore.studio.core":"1.0.0"}}"#,
        )
        .unwrap();
        let error = check_dependency_boundary(&opts).unwrap_err();
        assert!(error.contains("stage_dependency_denied"));
        assert!(error.contains("com.gamecore.gameplay.world"));
        assert!(error.contains("com.gamecore.studio.core"));
        std::fs::write(package.join("package.json"), r#"{"dependencies":{}}"#).unwrap();
        assert!(check_dependency_boundary(&opts).is_ok());
    }

    #[test]
    fn r2_11_stage_int_shared_analyzer_fixture_and_strict_results() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../stage/analyzer/Tests/Fixtures");
        let request: Request =
            serde_json::from_slice(&std::fs::read(root.join("request.json")).unwrap()).unwrap();
        assert_eq!(request.schema, "gamecore.stage.analyze/1");
        assert_eq!(request.policy.mode, "D1");
        let fixture = std::env::var_os("STAGE_CONTRACT_FIXTURE_OUT")
            .map(PathBuf::from)
            .unwrap_or(root.join("result.json"));
        let bytes = std::fs::read(fixture).unwrap();
        assert!(passing(&bytes));
        for invalid in [
            r#"{"schema":"gamecore.stage.findings/1","findings":[]}"#,
            r#"{"schema":"old","pass":true,"findings":[]}"#,
            r#"{"schema":"gamecore.stage.findings/1","pass":true,"findings":[],"extra":1}"#,
            r#"{"schema":"gamecore.stage.findings/1","pass":true,"pass":true,"findings":[]}"#,
            r#"{"schema":"gamecore.stage.findings/1","pass":true,"findings":[{"rule":"SG003","file":"Bad.cs","line":1,"message":"refused"}]}"#,
        ] {
            assert!(!passing(invalid.as_bytes()), "{invalid}");
        }
    }
}
