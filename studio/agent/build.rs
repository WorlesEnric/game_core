//! Build checks that only warn:
//!
//! 1. `ETOS_PIN` names the commit the vendored SDK was copied from (the `Commit` row of
//!    `vendor/README.md`, written by `vendor-etos-sdk.sh`); compared by prefix, so a short
//!    pin works too;
//! 2. each built-in contract schema in `schemas/` matches the canonical one in
//!    `docs/studio/schemas/` when that file exists.

use std::path::Path;

/// The commit in the `| Commit | `<sha>` |` row of vendor/README.md.
fn vendored_commit(readme: &str) -> Option<String> {
    readme.lines().find_map(|line| {
        let rest = line.trim().strip_prefix("| Commit |")?;
        let sha: String = rest.chars().filter(|c| c.is_ascii_hexdigit()).collect();
        (!sha.is_empty()).then_some(sha)
    })
}

fn main() {
    let manifest = std::env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".into());
    let here = Path::new(&manifest);
    println!("cargo:rerun-if-changed=ETOS_PIN");
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=vendor/README.md");

    let pin = std::fs::read_to_string(here.join("ETOS_PIN"))
        .map(|s| s.trim().to_string())
        .unwrap_or_default();
    let vendored = std::fs::read_to_string(here.join("vendor/README.md"))
        .ok()
        .and_then(|r| vendored_commit(&r));
    match vendored {
        None => println!(
            "cargo:warning=vendor/README.md names no commit; run vendor-etos-sdk.sh <etos-checkout>"
        ),
        Some(v) if pin.is_empty() => {
            println!("cargo:warning=ETOS_PIN is empty; the vendored SDK is from {v}")
        }
        Some(v) if !(v.starts_with(&pin) || pin.starts_with(&v)) => println!(
            "cargo:warning=the vendored SDK is from {v}, but ETOS_PIN says {pin}; re-run vendor-etos-sdk.sh"
        ),
        Some(_) => {}
    }

    for name in [
        "change-set.schema.json",
        "selection-snapshot.schema.json",
        "semantic-index.schema.json",
        "tool-catalog.schema.json",
        "diagnostic.schema.json",
        "authoring-ref.schema.json",
    ] {
        println!("cargo:rerun-if-changed=schemas/{name}");
        let canonical = here.join("../../docs/studio/schemas").join(name);
        if canonical.exists() {
            println!("cargo:rerun-if-changed={}", canonical.display());
            let a = std::fs::read(&canonical).unwrap_or_default();
            let b = std::fs::read(here.join("schemas").join(name)).unwrap_or_default();
            if a != b {
                println!(
                    "cargo:warning=schemas/{name} differs from docs/studio/schemas/{name}; copy it again"
                );
            }
        }
    }
}
