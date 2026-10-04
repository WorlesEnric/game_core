//! Build checks that only warn:
//!
//! 1. the etos checkout the SDK path dependency points at (`../../../etos`) is at the commit
//!    recorded in `ETOS_PIN` (compared by prefix, so a short pin works too);
//! 2. the vendored change-set schema (`schemas/change-set.schema.json`) matches the
//!    canonical one in `docs/studio/schemas/` once that file exists.

use std::path::Path;
use std::process::Command;

fn main() {
    let manifest = std::env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".into());
    let here = Path::new(&manifest);
    println!("cargo:rerun-if-changed=ETOS_PIN");
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=schemas/change-set.schema.json");

    let etos = here.join("../../../etos");
    let pin = std::fs::read_to_string(here.join("ETOS_PIN"))
        .map(|s| s.trim().to_string())
        .unwrap_or_default();
    let head_file = etos.join(".git/HEAD");
    if head_file.exists() {
        println!("cargo:rerun-if-changed={}", head_file.display());
    }
    let head = Command::new("git")
        .arg("-C")
        .arg(&etos)
        .args(["rev-parse", "HEAD"])
        .output();
    match head {
        Ok(out) if out.status.success() => {
            let head = String::from_utf8_lossy(&out.stdout).trim().to_string();
            if pin.is_empty() {
                println!("cargo:warning=ETOS_PIN is empty; cannot check the etos checkout");
            } else if !(head.starts_with(&pin) || pin.starts_with(&head)) {
                println!(
                    "cargo:warning=etos checkout at {} is {head}, but ETOS_PIN says {pin}",
                    etos.display()
                );
            }
        }
        _ => println!(
            "cargo:warning=cannot read the etos commit at {} (git rev-parse failed); ETOS_PIN is {pin}",
            etos.display()
        ),
    }

    let canonical = here.join("../../docs/studio/schemas/change-set.schema.json");
    if canonical.exists() {
        println!("cargo:rerun-if-changed={}", canonical.display());
        let a = std::fs::read(&canonical).unwrap_or_default();
        let b = std::fs::read(here.join("schemas/change-set.schema.json")).unwrap_or_default();
        if a != b {
            println!(
                "cargo:warning=schemas/change-set.schema.json differs from docs/studio/schemas/change-set.schema.json; re-vendor it"
            );
        }
    }
}
