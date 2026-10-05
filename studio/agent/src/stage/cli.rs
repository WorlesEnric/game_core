//! `gamecore-studio stage ...`: the staging lane from a shell (host operators, `stage.sh`,
//! `w-mech-01.sh`, the ignored `stage_real` test).
//!
//! ```text
//! gamecore-studio stage run <slot> [--candidate DIR | --package-dir DIR --change-set-id ID]
//!                                  [--source-project DIR] [--root DIR] [--repo DIR]
//!                                  [--steps scan,checkers,...] [--budget-s N] [--force]
//!                                  [--verdict-out FILE]
//! gamecore-studio stage gc [--root DIR] [--max-age-days N]
//! gamecore-studio stage discard <slot> [--root DIR]
//! gamecore-studio stage scan <package-dir> [--package NAME] [--allow-unsafe REASON] [--repo DIR]
//! ```
//!
//! `run` prints one line per step on stderr and the verdict's canonical JSON as the last
//! stdout line; it exits 0 for a passing verdict, 1 for a failing one and 2 when no verdict
//! could be made (bad usage, busy slot, a candidate that cannot be staged).

use std::collections::BTreeSet;
use std::path::PathBuf;
use std::time::Duration;

use serde_json::json;

use super::pipeline::{self, SlotSource, StageOptions};
use super::scan::{self, ScanContext};
use super::slot;
use super::verdict::{STEP_IDS, StepStatus};

const USAGE: &str = "usage:
  gamecore-studio stage run <slot> [--candidate DIR | --package-dir DIR --change-set-id ID]
                                   [--source-project DIR] [--root DIR] [--repo DIR]
                                   [--steps scan,checkers,...] [--budget-s N] [--force] [--verdict-out FILE]
  gamecore-studio stage gc [--root DIR] [--max-age-days N]
  gamecore-studio stage discard <slot> [--root DIR]
  gamecore-studio stage scan <package-dir> [--package NAME] [--allow-unsafe REASON] [--repo DIR]";

struct Args {
    positional: Vec<String>,
    options: Vec<(String, Option<String>)>,
}

const FLAGS: &[&str] = &["--force"];

fn parse(args: &[String]) -> Result<Args, String> {
    let mut out = Args {
        positional: Vec::new(),
        options: Vec::new(),
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        if let Some(name) = a.strip_prefix("--") {
            if FLAGS.contains(&a.as_str()) {
                out.options.push((name.to_string(), None));
            } else {
                let v = it.next().ok_or_else(|| format!("{a} needs a value"))?;
                out.options.push((name.to_string(), Some(v.clone())));
            }
        } else {
            out.positional.push(a.clone());
        }
    }
    Ok(out)
}

impl Args {
    fn get(&self, name: &str) -> Option<&str> {
        self.options
            .iter()
            .rev()
            .find(|(n, _)| n == name)
            .and_then(|(_, v)| v.as_deref())
    }

    fn flag(&self, name: &str) -> bool {
        self.options.iter().any(|(n, _)| n == name)
    }

    fn check(&self, allowed: &[&str]) -> Result<(), String> {
        match self
            .options
            .iter()
            .find(|(n, _)| !allowed.contains(&n.as_str()))
        {
            Some((n, _)) => Err(format!("unknown option --{n}")),
            None => Ok(()),
        }
    }
}

fn fail(message: &str) -> i32 {
    eprintln!("gamecore-studio stage: {message}");
    println!("{}", json!({"ok": false, "error": message}));
    2
}

/// Entry point; `args` are the words after `stage`. Returns the process exit code.
pub fn main(args: &[String]) -> i32 {
    let Some(command) = args.first() else {
        eprintln!("{USAGE}");
        return 2;
    };
    let parsed = match parse(&args[1..]) {
        Ok(p) => p,
        Err(e) => return fail(&e),
    };
    match command.as_str() {
        "run" => run(&parsed),
        "gc" => gc(&parsed),
        "discard" => discard(&parsed),
        "scan" => scan_cmd(&parsed),
        "-h" | "--help" | "help" => {
            eprintln!("{USAGE}");
            0
        }
        other => fail(&format!("unknown stage command {other:?}\n{USAGE}")),
    }
}

fn root_of(a: &Args) -> PathBuf {
    a.get("root")
        .map(PathBuf::from)
        .unwrap_or_else(slot::default_root)
}

fn run(a: &Args) -> i32 {
    if let Err(e) = a.check(&[
        "candidate",
        "package-dir",
        "change-set-id",
        "source-project",
        "root",
        "repo",
        "steps",
        "budget-s",
        "force",
        "verdict-out",
    ]) {
        return fail(&e);
    }
    let [slot_id] = a.positional.as_slice() else {
        return fail(&format!("run takes exactly one slot id\n{USAGE}"));
    };
    let Some(repo) = a
        .get("repo")
        .map(PathBuf::from)
        .or_else(pipeline::discover_repo)
    else {
        return fail("no game_core checkout found; pass --repo or set GAMECORE_STAGE_REPO");
    };
    let source = match (
        a.get("candidate"),
        a.get("package-dir"),
        a.get("change-set-id"),
    ) {
        (Some(c), None, None) => SlotSource::Candidate(PathBuf::from(c)),
        (None, Some(d), Some(cs)) => SlotSource::PackageDir {
            dir: PathBuf::from(d),
            change_set_id: cs.to_string(),
        },
        (None, None, None) => SlotSource::Existing,
        _ => return fail("use --candidate DIR, or --package-dir DIR with --change-set-id ID"),
    };
    let mut opts = StageOptions::from_env(&repo, slot_id, source);
    opts.root = root_of(a);
    opts.force = a.flag("force");
    if let Some(p) = a.get("source-project") {
        opts.source_project = PathBuf::from(p);
    }
    if let Some(b) = a.get("budget-s") {
        match b.parse::<u64>() {
            Ok(s) if s > 0 => opts.budget = Duration::from_secs(s),
            _ => return fail("--budget-s takes whole seconds > 0"),
        }
    }
    if let Some(list) = a.get("steps") {
        let steps: BTreeSet<String> = list.split(',').map(|s| s.trim().to_string()).collect();
        if let Some(bad) = steps.iter().find(|s| !STEP_IDS.contains(&s.as_str())) {
            return fail(&format!(
                "unknown step {bad:?}; steps are {}",
                STEP_IDS.join(", ")
            ));
        }
        opts.steps = Some(steps);
    }
    eprintln!(
        "-- stage {} (root {}, source {}, budget {} s)",
        opts.slot,
        opts.root.display(),
        opts.source_project.display(),
        opts.budget.as_secs()
    );
    let verdict = match pipeline::run_stage(&opts) {
        Ok(v) => v,
        Err(e) => return fail(&pipeline::redact_all(&e)),
    };
    for s in &verdict.steps {
        let status = match s.status {
            StepStatus::Pass => "pass",
            StepStatus::Fail => "FAIL",
            StepStatus::Skipped => "skip",
        };
        eprintln!(
            "   {:<15} {:<4} {:>8} ms  {}",
            s.id, status, s.duration_ms, s.detail
        );
    }
    for h in &verdict.forbidden_hits {
        eprintln!(
            "   forbidden {} {}:{} {}",
            h.rule, h.path, h.line, h.excerpt
        );
    }
    let reference = verdict.verdict_ref();
    eprintln!(
        "RESULT stage {}: {} ({} ms of {} ms; verdict {} at {})",
        verdict.slot,
        if verdict.pass { "PASS" } else { "FAIL" },
        verdict.duration_ms,
        verdict.budget_ms,
        reference,
        opts.slot_dir().join("out/verdict.json").display()
    );
    if let Some(path) = a.get("verdict-out")
        && let Err(e) = std::fs::write(path, verdict.bytes())
    {
        eprintln!("gamecore-studio stage: cannot write {path}: {e}");
    }
    println!("{}", String::from_utf8_lossy(&verdict.bytes()));
    if verdict.pass { 0 } else { 1 }
}

fn gc(a: &Args) -> i32 {
    if let Err(e) = a.check(&["root", "max-age-days"]) {
        return fail(&e);
    }
    let days = match a.get("max-age-days").map(str::parse::<u64>) {
        None => 7,
        Some(Ok(d)) => d,
        Some(Err(_)) => return fail("--max-age-days takes whole days"),
    };
    let removed = slot::gc(&root_of(a), Duration::from_secs(days * 24 * 3600));
    println!("{}", json!({"ok": true, "removed": removed}));
    0
}

fn discard(a: &Args) -> i32 {
    if let Err(e) = a.check(&["root"]) {
        return fail(&e);
    }
    let [slot_id] = a.positional.as_slice() else {
        return fail("discard takes exactly one slot id");
    };
    if !slot::valid_slot_id(slot_id) {
        return fail("not a slot id");
    }
    let dir = root_of(a).join(slot_id);
    if slot::is_locked(&dir) {
        return fail("the slot is being staged");
    }
    match slot::remove_slot(&dir) {
        Ok(()) => {
            println!("{}", json!({"ok": true, "discarded": slot_id}));
            0
        }
        Err(e) => fail(&format!("cannot remove {}: {e}", dir.display())),
    }
}

fn scan_cmd(a: &Args) -> i32 {
    if let Err(e) = a.check(&["package", "allow-unsafe", "repo"]) {
        return fail(&e);
    }
    let [dir] = a.positional.as_slice() else {
        return fail("scan takes exactly one package directory");
    };
    // The documented exemptions of the checkout's allowlist apply, as in `stage run`.
    let exemptions = match a
        .get("repo")
        .map(PathBuf::from)
        .or_else(pipeline::discover_repo)
    {
        Some(repo) => match scan::load_exemptions(&repo.join("studio/stage/allowlist.json")) {
            Ok(list) => list,
            Err(e) => return fail(&e),
        },
        None => Vec::new(),
    };
    let ctx = ScanContext {
        package: a.get("package").unwrap_or_default().to_string(),
        allow_unsafe: a.get("allow-unsafe").map(str::to_string),
        blobs: BTreeSet::new(),
        exemptions,
    };
    match scan::scan_package(std::path::Path::new(dir), &ctx) {
        Ok(report) => {
            for h in &report.hits {
                eprintln!("   {} {}:{} {}", h.rule, h.path, h.line, h.excerpt);
            }
            for (id, h) in &report.exempted {
                eprintln!("   exempt[{id}] {} {}:{}", h.rule, h.path, h.line);
            }
            println!(
                "{}",
                serde_json::to_string(&report).unwrap_or_else(|_| "{}".into())
            );
            i32::from(!report.hits.is_empty())
        }
        Err(e) => fail(&format!("cannot scan {dir}: {e}")),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn words(s: &str) -> Vec<String> {
        s.split_whitespace().map(str::to_string).collect()
    }

    #[test]
    fn arguments_parse_and_unknown_options_are_refused() {
        let a = parse(&words("s1 --candidate /c --force --steps scan,checkers")).unwrap();
        assert_eq!(a.positional, vec!["s1"]);
        assert_eq!(a.get("candidate"), Some("/c"));
        assert!(a.flag("force"));
        assert!(a.check(&["candidate", "force", "steps"]).is_ok());
        assert!(a.check(&["candidate"]).is_err());
        assert!(parse(&words("--root")).is_err());
    }

    #[test]
    fn bad_invocations_exit_2() {
        assert_eq!(main(&[]), 2);
        assert_eq!(main(&words("nope")), 2);
        assert_eq!(main(&words("run")), 2);
        assert_eq!(main(&words("run s1 --steps bogus --repo /nonexistent")), 2);
        assert_eq!(main(&words("discard ../x")), 2);
    }

    #[test]
    fn scan_and_gc_and_discard_work_on_a_temporary_root() {
        let dir = tempfile::tempdir().unwrap();
        let pkg = dir.path().join("pkg");
        std::fs::create_dir_all(pkg.join("Runtime")).unwrap();
        std::fs::write(
            pkg.join("Runtime/A.cs"),
            "class A { void F() { System.Diagnostics.Process.Start(\"x\"); } }",
        )
        .unwrap();
        assert_eq!(
            main(&[String::from("scan"), pkg.to_string_lossy().into_owned()]),
            1
        );
        std::fs::write(pkg.join("Runtime/A.cs"), "class A { }").unwrap();
        assert_eq!(
            main(&[String::from("scan"), pkg.to_string_lossy().into_owned()]),
            0
        );

        let root = dir.path().join("root");
        std::fs::create_dir_all(root.join("s1")).unwrap();
        let r = root.to_string_lossy().into_owned();
        assert_eq!(main(&[String::from("gc"), "--root".into(), r.clone()]), 0);
        assert!(root.join("s1").exists());
        assert_eq!(
            main(&[String::from("discard"), "s1".into(), "--root".into(), r]),
            0
        );
        assert!(!root.join("s1").exists());
    }
}
