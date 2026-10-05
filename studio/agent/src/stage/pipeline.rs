//! The stage pipeline (P2.4): one candidate, one slot, seven steps, one verdict.
//!
//! 1. `scan`            forbidden-content scan of the candidate package ([`super::scan`]);
//! 2. `checkers`        the repository checkers on the slot (`studio/stage/slot-checks.py`);
//! 3. `dotnet`          `dotnet build` + `dotnet test` of the Unity-free rules half (skipped as
//!    not applicable when the package has none);
//! 4. `unity-editmode`  batchmode Unity: compile + EditMode tests (the candidate's own tests and
//!    the harness catalog probe), under the host-wide Unity lock (`studio/tools/unity-batch.sh`:
//!    at most one Editor per stage, 600 s silence watchdog, one retry on the known hang);
//! 5. `playmode-smoke`  batchmode Unity: PlayMode tests, the harness smoke runner boots a tiny
//!    world with the mechanism mounted, runs its `smokeTest` entry for N frames and asserts one
//!    sanctioned pump per frame and no exception;
//! 6. `determinism`     the smoke runs twice on fresh worlds; the canonical slot hashes must agree;
//! 7. `budget`          the whole stage (slot refresh included) within B-STAGE (6 min), else the
//!    stage fails with `stage_failed{timeout}`.
//!
//! Code never executes before it passed the scan and the checkers: a failed `scan` or
//! `checkers` step skips every later step that runs candidate code, and a failed step skips the
//! steps that depend on it. Children run with `env_clear()` plus [`super::env::stage_env`]; every
//! log is redacted before it is written and hashed (`logRef`).

use std::collections::BTreeSet;
use std::fs::File;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use serde_json::{Value, json};

use super::env::stage_env;
use super::scan::{self, ScanContext, mask_secrets};
use super::slot::{self, SlotLock};
use super::verdict::{
    B_STAGE_MS, CatalogDelta, CoveredArtifact, PackageFile, RunnerInfo, STEP_IDS, StageVerdict,
    StepResult, StepStatus, VERDICT_SCHEMA,
};
use crate::redact::redact;
use crate::util::{now_ms, sha256_hex};

/// Where the candidate comes from.
#[derive(Debug, Clone)]
pub enum SlotSource {
    /// The slot exists already (`stage run <slot>`).
    Existing,
    /// A directory with `change-set.json` and its artifacts: the slot is made or refreshed.
    Candidate(PathBuf),
    /// A bare package directory (legacy `stage.sh <slot> <package-dir>`).
    PackageDir {
        /// The package directory.
        dir: PathBuf,
        /// The change set it belongs to.
        change_set_id: String,
    },
}

/// External tools the pipeline runs.
#[derive(Debug, Clone)]
pub struct Toolchain {
    /// Python 3 interpreter.
    pub python: PathBuf,
    /// The dotnet driver.
    pub dotnet: PathBuf,
    /// `studio/tools/unity-batch.sh` (the shared Unity lock runner).
    pub unity_batch: PathBuf,
    /// `studio/stage/make-slot.py`.
    pub make_slot: PathBuf,
    /// `studio/stage/slot-checks.py`.
    pub slot_checks: PathBuf,
}

impl Toolchain {
    /// The tools of a repository checkout (`GAMECORE_STAGE_PYTHON`, `DOTNET` override).
    pub fn of_repo(repo: &Path) -> Toolchain {
        let home = std::env::var_os("HOME")
            .map(PathBuf::from)
            .unwrap_or_default();
        let dotnet = std::env::var_os("DOTNET")
            .map(PathBuf::from)
            .or_else(|| {
                let p = home.join(".dotnet").join("dotnet");
                p.is_file().then_some(p)
            })
            .unwrap_or_else(|| PathBuf::from("dotnet"));
        Toolchain {
            python: std::env::var_os("GAMECORE_STAGE_PYTHON")
                .map(PathBuf::from)
                .unwrap_or_else(|| PathBuf::from("python3")),
            dotnet,
            unity_batch: repo.join("studio/tools/unity-batch.sh"),
            make_slot: repo.join("studio/stage/make-slot.py"),
            slot_checks: repo.join("studio/stage/slot-checks.py"),
        }
    }
}

/// Everything one stage needs.
#[derive(Debug, Clone)]
pub struct StageOptions {
    /// HTTP request's source revision, rechecked after slot preparation.
    pub expected_source_revision: Option<String>,
    /// Trusted execution boundary for all candidate compilation and tests.
    pub sandbox: super::sandbox::Sandbox,
    /// Slot root.
    pub root: PathBuf,
    /// The repository checkout (checkers, template, tools).
    pub repo: PathBuf,
    /// The source Unity project (kernel/gameplay pins, settings, stage inputs).
    pub source_project: PathBuf,
    /// Slot id.
    pub slot: String,
    /// The candidate.
    pub source: SlotSource,
    /// Steps to run (`None`: all). A partial run never passes.
    pub steps: Option<BTreeSet<String>>,
    /// The whole-stage budget (B-STAGE).
    pub budget: Duration,
    /// Upper bound of one Unity attempt (the remaining budget bounds it too).
    pub unity_attempt: Duration,
    /// Replace a slot that holds another change set.
    pub force: bool,
    /// Tools.
    pub tools: Toolchain,
}

impl StageOptions {
    /// Options from the environment: `GAMECORE_STAGE_ROOT`, `GAMECORE_STAGE_SOURCE_PROJECT`
    /// (default `<repo>/games/hollowmere`), `GAMECORE_STAGE_BUDGET_S` (360),
    /// `GAMECORE_STAGE_UNITY_TIMEOUT_S` (300).
    pub fn from_env(repo: &Path, slot: &str, source: SlotSource) -> StageOptions {
        let secs = |name: &str, default: u64| {
            std::env::var(name)
                .ok()
                .and_then(|v| v.trim().parse::<u64>().ok())
                .filter(|v| *v > 0)
                .unwrap_or(default)
        };
        StageOptions {
            expected_source_revision: None,
            sandbox: super::sandbox::Sandbox::defaults(
                &slot::default_root().join(slot),
                &slot::default_root().join("_warm"),
                repo,
            ),
            root: slot::default_root(),
            repo: repo.to_path_buf(),
            source_project: std::env::var_os("GAMECORE_STAGE_SOURCE_PROJECT")
                .map(PathBuf::from)
                .unwrap_or_else(|| repo.join("games/hollowmere")),
            slot: slot.to_string(),
            source,
            steps: None,
            budget: Duration::from_secs(secs("GAMECORE_STAGE_BUDGET_S", B_STAGE_MS / 1000)),
            unity_attempt: Duration::from_secs(secs("GAMECORE_STAGE_UNITY_TIMEOUT_S", 300)),
            force: false,
            tools: Toolchain::of_repo(repo),
        }
    }

    /// The slot directory.
    pub fn slot_dir(&self) -> PathBuf {
        self.root.join(&self.slot)
    }
}

/// The repository checkout this binary belongs to: `GAMECORE_STAGE_REPO`, else the first
/// ancestor of the executable (or of the build directory) holding `studio/stage/make-slot.py`.
pub fn discover_repo() -> Option<PathBuf> {
    if let Some(v) = std::env::var_os("GAMECORE_STAGE_REPO").filter(|v| !v.is_empty()) {
        return Some(PathBuf::from(v));
    }
    let has = |d: &Path| d.join("studio/stage/make-slot.py").is_file();
    if let Ok(exe) = std::env::current_exe().and_then(|e| e.canonicalize())
        && let Some(d) = exe.ancestors().find(|d| has(d))
    {
        return Some(d.to_path_buf());
    }
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .find(|d| has(d))
        .map(Path::to_path_buf)
}

/// `redact` plus provider-key masking: what every log line goes through.
pub fn redact_all(text: &str) -> String {
    mask_secrets(&redact(text))
}

/// The outcome of one child process.
#[derive(Debug, Clone)]
pub struct ChildOutcome {
    /// Exit code (`None` when killed).
    pub code: Option<i32>,
    /// Killed at the deadline.
    pub timed_out: bool,
    /// Its stdout+stderr, redacted.
    pub output: String,
    /// Wall time.
    pub elapsed: Duration,
}

impl ChildOutcome {
    /// Exit 0, not killed.
    pub fn ok(&self) -> bool {
        self.code == Some(0) && !self.timed_out
    }

    /// The last JSON object line of the output.
    pub fn last_json(&self) -> Option<Value> {
        self.output
            .lines()
            .rev()
            .filter_map(|l| serde_json::from_str::<Value>(l.trim()).ok())
            .find(Value::is_object)
    }
}

fn kill_group(pid: u32, signal: &str) {
    let _ = Command::new("/bin/kill")
        .env_clear()
        .envs(stage_env())
        .args(["-s", signal, "--"])
        .arg(format!("-{pid}"))
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status();
}

/// Run `cmd` (environment cleared to the stage allowlist plus `extra_env`, its own process
/// group, stdin closed) with output captured through `scratch`, killed at `timeout`.
pub fn run_child(
    cmd: &mut Command,
    extra_env: &[(&str, &str)],
    scratch: &Path,
    timeout: Duration,
) -> ChildOutcome {
    use std::os::unix::process::CommandExt;
    let started = Instant::now();
    let fail = |message: String| ChildOutcome {
        code: None,
        timed_out: false,
        output: redact_all(&message),
        elapsed: started.elapsed(),
    };
    let file = match File::create(scratch) {
        Ok(f) => f,
        Err(e) => return fail(format!("cannot create {}: {e}", scratch.display())),
    };
    let err = match file.try_clone() {
        Ok(f) => f,
        Err(e) => return fail(format!("cannot capture output: {e}")),
    };
    cmd.env_clear()
        .envs(stage_env())
        .envs(extra_env.iter().map(|(k, v)| (*k, *v)))
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .process_group(0);
    let mut child = match cmd.spawn() {
        Ok(c) => c,
        Err(e) => return fail(format!("cannot start {:?}: {e}", cmd.get_program())),
    };
    let stdout = child.stdout.take();
    let stderr = child.stderr.take();
    let out_writer = std::thread::spawn(move || {
        if let Some(pipe) = stdout {
            crate::redact::copy_redacted(pipe, crate::redact::BoundedLog::new(file))
        } else {
            Ok(())
        }
    });
    let err_writer = std::thread::spawn(move || {
        if let Some(pipe) = stderr {
            crate::redact::copy_redacted(pipe, crate::redact::BoundedLog::new(err))
        } else {
            Ok(())
        }
    });
    let pid = child.id();
    let mut timed_out = false;
    let code = loop {
        match child.try_wait() {
            Ok(Some(status)) => break status.code(),
            Ok(None) => {}
            Err(_) => break None,
        }
        if started.elapsed() >= timeout {
            timed_out = true;
            kill_group(pid, "TERM");
            let grace = Instant::now();
            while grace.elapsed() < Duration::from_secs(15) {
                if let Ok(Some(_)) = child.try_wait() {
                    break;
                }
                std::thread::sleep(Duration::from_millis(200));
            }
            kill_group(pid, "KILL");
            let _ = child.kill();
            let _ = child.wait();
            break None;
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    // Terminate descendants even when their parent exited before them.
    kill_group(pid, "KILL");
    let written =
        matches!(out_writer.join(), Ok(Ok(()))) && matches!(err_writer.join(), Ok(Ok(())));
    let code = if written { code } else { None };
    let raw = std::fs::read(scratch).unwrap_or_default();
    let _ = std::fs::remove_file(scratch);
    let mut output = redact_all(&String::from_utf8_lossy(&raw));
    if timed_out {
        output.push_str(&format!(
            "\n[stage] killed after {} s (stage budget)\n",
            timeout.as_secs()
        ));
    }
    ChildOutcome {
        code,
        timed_out,
        output,
        elapsed: started.elapsed(),
    }
}

/// NUnit 3 result totals (the Unity test runner's `-testResults` XML).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TestTotals {
    /// Test cases.
    pub total: u64,
    /// Passed.
    pub passed: u64,
    /// Failed.
    pub failed: u64,
    /// Skipped / ignored.
    pub skipped: u64,
    /// Full names of the failed cases (at most 20).
    pub failures: Vec<String>,
}

fn xml_unescape(s: &str) -> String {
    s.replace("&quot;", "\"")
        .replace("&apos;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
}

fn xml_attr(tag: &str, name: &str) -> Option<String> {
    let needle = format!(" {name}=\"");
    let start = tag.find(&needle)? + needle.len();
    let end = tag[start..].find('"')? + start;
    Some(xml_unescape(&tag[start..end]))
}

fn xml_tags<'a>(xml: &'a str, element: &str) -> Vec<&'a str> {
    let open = format!("<{element} ");
    let mut out = Vec::new();
    let mut rest = xml;
    while let Some(pos) = rest.find(&open) {
        let tail = &rest[pos..];
        let end = tail.find('>').unwrap_or(tail.len());
        out.push(&tail[..end]);
        rest = &tail[end..];
    }
    out
}

/// Parse the totals of an NUnit 3 `test-run`.
pub fn parse_nunit(xml: &str) -> Option<TestTotals> {
    let run = *xml_tags(xml, "test-run").first()?;
    let num = |n: &str| {
        xml_attr(run, n)
            .and_then(|v| v.parse::<u64>().ok())
            .unwrap_or(0)
    };
    let failures = xml_tags(xml, "test-case")
        .into_iter()
        .filter(|t| xml_attr(t, "result").as_deref() == Some("Failed"))
        .filter_map(|t| xml_attr(t, "fullname").or_else(|| xml_attr(t, "name")))
        .take(20)
        .collect();
    Some(TestTotals {
        total: num("total"),
        passed: num("passed"),
        failed: num("failed"),
        skipped: num("skipped"),
        failures,
    })
}

/// `Failed: n, Passed: n, Skipped: n, Total: n` of a `dotnet test` summary.
pub fn parse_dotnet_summary(output: &str) -> Option<TestTotals> {
    let line = output
        .lines()
        .rev()
        .find(|l| l.contains("Failed:") && l.contains("Passed:") && l.contains("Total:"))?;
    let num = |key: &str| -> u64 {
        line.find(key)
            .map(|i| &line[i + key.len()..])
            .map(|t| {
                t.trim_start()
                    .chars()
                    .take_while(char::is_ascii_digit)
                    .collect::<String>()
            })
            .and_then(|d| d.parse().ok())
            .unwrap_or(0)
    };
    Some(TestTotals {
        total: num("Total:"),
        passed: num("Passed:"),
        failed: num("Failed:"),
        skipped: num("Skipped:"),
        failures: Vec::new(),
    })
}

/// Differences between the package directory and the slot manifest's declared files.
pub fn integrity_problems(package_dir: &Path, declared: &[PackageFile]) -> Vec<String> {
    let mut present = std::collections::BTreeMap::new();
    let mut stack = vec![package_dir.to_path_buf()];
    while let Some(d) = stack.pop() {
        let Ok(entries) = std::fs::read_dir(&d) else {
            continue;
        };
        for e in entries.filter_map(Result::ok) {
            let p = e.path();
            if e.file_type().is_ok_and(|t| t.is_dir()) {
                stack.push(p);
            } else if let Ok(rel) = p.strip_prefix(package_dir) {
                let digest = std::fs::read(&p)
                    .map(|b| sha256_hex(&b))
                    .unwrap_or_default();
                present.insert(rel.to_string_lossy().replace('\\', "/"), digest);
            }
        }
    }
    let mut problems = Vec::new();
    for f in declared {
        match present.remove(&f.path) {
            None => problems.push(format!("{} is missing", f.path)),
            Some(d) if d != f.sha256 => problems.push(format!("{} changed", f.path)),
            Some(_) => {}
        }
    }
    problems.extend(present.into_keys().map(|p| format!("{p} appeared")));
    problems.sort();
    problems
}

struct StepLog {
    text: String,
}

impl StepLog {
    fn new() -> StepLog {
        StepLog {
            text: String::new(),
        }
    }

    fn line(&mut self, line: impl AsRef<str>) {
        self.text.push_str(&redact_all(line.as_ref()));
        self.text.push('\n');
    }

    fn child(&mut self, title: &str, out: &ChildOutcome) {
        self.line(format!(
            "$ {title}  (exit {:?}{}, {} ms)",
            out.code,
            if out.timed_out { ", killed" } else { "" },
            out.elapsed.as_millis()
        ));
        self.text.push_str(&out.output);
        if !out.output.ends_with('\n') {
            self.text.push('\n');
        }
    }
}

struct Run<'a> {
    opts: &'a StageOptions,
    slot_dir: PathBuf,
    record: Value,
    deadline: Instant,
    steps: Vec<StepResult>,
    timed_out: bool,
    hits: Vec<scan::Hit>,
    delta: CatalogDelta,
}

impl Run<'_> {
    fn remaining(&self) -> Duration {
        self.deadline.saturating_duration_since(Instant::now())
    }

    fn requested(&self, id: &str) -> bool {
        self.opts.steps.as_ref().is_none_or(|s| s.contains(id))
    }

    fn out_dir(&self) -> PathBuf {
        self.slot_dir.join("out")
    }

    fn package(&self) -> String {
        self.record["package"]["name"]
            .as_str()
            .unwrap_or_default()
            .to_string()
    }

    fn package_dir(&self) -> PathBuf {
        self.slot_dir
            .join("project")
            .join("Packages")
            .join(self.package())
    }

    fn declared(&self) -> Vec<PackageFile> {
        serde_json::from_value(self.record["files"].clone()).unwrap_or_default()
    }

    fn scratch(&self, id: &str) -> PathBuf {
        self.out_dir().join("logs").join(format!(".{id}.raw"))
    }

    fn finish(&mut self, mut result: StepResult, log: StepLog) {
        let path = self
            .out_dir()
            .join("logs")
            .join(format!("{}.log", result.id));
        if !log.text.is_empty() && std::fs::write(&path, log.text.as_bytes()).is_ok() {
            result.log_ref = Some(sha256_hex(log.text.as_bytes()));
        }
        result.detail = redact_all(&result.detail);
        self.steps.push(result);
    }

    fn skip(&mut self, id: &str, why: &str) {
        self.steps
            .push(StepResult::new(id, StepStatus::Skipped, 0, why));
    }

    fn budget_gone(&mut self, id: &str) -> bool {
        if self.remaining().is_zero() {
            self.timed_out = true;
            self.steps.push(StepResult::new(
                id,
                StepStatus::Fail,
                0,
                "the stage budget was exhausted before this step",
            ));
            return true;
        }
        false
    }

    // -- (a) ------------------------------------------------------------------------------
    fn step_scan(&mut self) {
        let t = Instant::now();
        let mut log = StepLog::new();
        let exemptions =
            match scan::load_exemptions(&self.opts.repo.join("studio/stage/allowlist.json")) {
                Ok(list) => list,
                Err(e) => {
                    let result = StepResult::new(
                        "scan",
                        StepStatus::Fail,
                        ms(t.elapsed()),
                        format!("the stage allowlist is invalid: {e}"),
                    );
                    self.finish(result, log);
                    return;
                }
            };
        let ctx = ScanContext {
            exemptions,
            package: self.package(),
            allow_unsafe: self.record["allowUnsafe"].as_str().map(str::to_string),
            blobs: self.record["blobs"]
                .as_array()
                .map(|a| {
                    a.iter()
                        .filter_map(|b| b.as_str().or_else(|| b["path"].as_str()))
                        .map(str::to_string)
                        .collect()
                })
                .unwrap_or_default(),
        };
        let result = match scan::scan_package(&self.package_dir(), &ctx) {
            Ok(report) => {
                for h in &report.hits {
                    log.line(format!("{} {}:{} {}", h.rule, h.path, h.line, h.excerpt));
                }
                for (id, h) in &report.exempted {
                    log.line(format!(
                        "exempt[{id}] {} {}:{} {}",
                        h.rule, h.path, h.line, h.excerpt
                    ));
                }
                log.line(format!(
                    "scanned {} file(s), {} byte(s): {} hit(s)",
                    report.files,
                    report.bytes,
                    report.hits.len()
                ));
                let status = if report.hits.is_empty() {
                    StepStatus::Pass
                } else {
                    StepStatus::Fail
                };
                let rules: BTreeSet<&str> = report.hits.iter().map(|h| h.rule.as_str()).collect();
                let detail = if report.hits.is_empty() {
                    format!("{} file(s) clean", report.files)
                } else {
                    format!(
                        "{} forbidden hit(s): {}",
                        report.hits.len(),
                        rules.into_iter().collect::<Vec<_>>().join(", ")
                    )
                };
                let mut exempted: std::collections::BTreeMap<&str, usize> = Default::default();
                for (id, _) in &report.exempted {
                    *exempted.entry(id.as_str()).or_default() += 1;
                }
                let facts = json!({"files": report.files, "bytes": report.bytes,
                    "hits": report.hits.len(), "exempted": exempted});
                self.hits = report.hits;
                StepResult {
                    facts,
                    ..StepResult::new("scan", status, ms(t.elapsed()), detail)
                }
            }
            Err(e) => StepResult::new(
                "scan",
                StepStatus::Fail,
                ms(t.elapsed()),
                format!("cannot scan the package: {e}"),
            ),
        };
        self.finish(result, log);
    }

    // -- (b) ------------------------------------------------------------------------------
    fn step_checkers(&mut self) {
        let t = Instant::now();
        let mut log = StepLog::new();
        let out = run_child(
            Command::new(&self.opts.tools.python)
                .arg(&self.opts.tools.slot_checks)
                .arg("--slot")
                .arg(&self.slot_dir)
                .arg("--repo")
                .arg(&self.opts.repo),
            &[],
            &self.scratch("checkers"),
            self.remaining(),
        );
        log.child("slot-checks.py", &out);
        self.timed_out |= out.timed_out;
        let facts = out.last_json().unwrap_or(Value::Null);
        let problems = facts["problems"].as_array().map_or(0, Vec::len);
        let (status, detail) = if out.ok() {
            (
                StepStatus::Pass,
                format!(
                    "{} C# file(s), {} asmdef(s), slot clean",
                    facts["csharpFiles"], facts["asmdefs"]
                ),
            )
        } else if out.timed_out {
            (StepStatus::Fail, "timed out".to_string())
        } else {
            let first = facts["problems"][0].as_str().unwrap_or("see the log");
            (
                StepStatus::Fail,
                format!("{problems} checker problem(s); first: {first}"),
            )
        };
        let mut facts = facts;
        if let Some(o) = facts.as_object_mut() {
            o.remove("problems");
            o.insert("problems".into(), json!(problems));
        }
        self.finish(
            StepResult {
                facts,
                ..StepResult::new("checkers", status, ms(t.elapsed()), detail)
            },
            log,
        );
    }

    // -- (c) ------------------------------------------------------------------------------
    fn step_dotnet(&mut self) {
        let t = Instant::now();
        let mut semantic_log = StepLog::new();
        let semantic = self.semantic_scan();
        semantic_log.child("Roslyn semantic analyzer", &semantic);
        if !semantic.ok() {
            self.finish(
                StepResult::new(
                    "dotnet",
                    StepStatus::Fail,
                    ms(t.elapsed()),
                    "semantic analyzer missing, failed, or reported forbidden code",
                ),
                semantic_log,
            );
            return;
        }
        let dotnet = self.record["dotnet"].clone();
        if !dotnet.is_object() {
            self.steps.push(StepResult {
                facts: json!({"notApplicable": true}),
                ..StepResult::new(
                    "dotnet",
                    StepStatus::Pass,
                    ms(t.elapsed()),
                    "semantic scan passed; the package has no Unity-free rules half",
                )
            });
            return;
        }
        let mut log = StepLog::new();
        let dir = self.slot_dir.join("dotnet");
        let build = self.opts.sandbox.run(
            Command::new(&self.opts.tools.dotnet)
                .arg("build")
                .arg(dir.join("Rules").join("Rules.csproj"))
                .args(["-nologo", "-v:q", "-p:NuGetAudit=false"])
                .current_dir(&dir),
            &self.scratch("dotnet-build"),
            self.remaining(),
        );
        log.child("dotnet build Rules.csproj", &build);
        self.timed_out |= build.timed_out;
        let has_tests = dotnet["tests"].is_string();
        let mut facts = json!({"assembly": dotnet["assembly"], "build": build.ok()});
        let (status, detail) = if !build.ok() {
            let first = build
                .output
                .lines()
                .find(|l| l.contains("error CS"))
                .unwrap_or("see the log")
                .trim()
                .to_string();
            (StepStatus::Fail, format!("dotnet build failed: {first}"))
        } else if !has_tests {
            (
                StepStatus::Pass,
                "rules half builds; it ships no dotnet tests".to_string(),
            )
        } else {
            let test = self.opts.sandbox.run(
                Command::new(&self.opts.tools.dotnet)
                    .arg("test")
                    .arg(dir.join("Rules.Tests").join("Rules.Tests.csproj"))
                    .args(["-nologo", "-p:NuGetAudit=false"])
                    .current_dir(&dir),
                &self.scratch("dotnet-test"),
                self.remaining(),
            );
            log.child("dotnet test Rules.Tests.csproj", &test);
            self.timed_out |= test.timed_out;
            let totals = parse_dotnet_summary(&test.output);
            if let (Some(o), Some(tt)) = (facts.as_object_mut(), &totals) {
                o.insert(
                    "tests".into(),
                    json!({"total": tt.total, "passed": tt.passed, "failed": tt.failed, "skipped": tt.skipped}),
                );
            }
            match totals {
                Some(tt) if test.ok() && tt.failed == 0 && tt.total > 0 => (
                    StepStatus::Pass,
                    format!("{} dotnet test(s) passed", tt.passed),
                ),
                Some(tt) => (
                    StepStatus::Fail,
                    format!("dotnet test: {} failed of {}", tt.failed, tt.total),
                ),
                None => (
                    StepStatus::Fail,
                    format!("dotnet test printed no summary (exit {:?})", test.code),
                ),
            }
        };
        self.finish(
            StepResult {
                facts,
                ..StepResult::new("dotnet", status, ms(t.elapsed()), detail)
            },
            log,
        );
    }

    fn semantic_scan(&self) -> ChildOutcome {
        let analyzer = self.opts.repo.join("studio/stage/analyzer");
        let output = self.out_dir().join("semantic-findings.json");
        let _ = std::fs::remove_file(&output);
        let mut cmd = Command::new(&self.opts.tools.dotnet);
        // Copy trusted analyzer source into the writable slot; never candidate selected.
        let target = self.slot_dir.join("semantic-analyzer");
        if target.exists() {
            let _ = slot::remove_slot(&target);
        }
        if !analyzer.is_dir()
            || !Command::new("/bin/cp")
                .env_clear()
                .envs(stage_env())
                .arg("-a")
                .arg(&analyzer)
                .arg(&target)
                .status()
                .is_ok_and(|s| s.success())
        {
            return ChildOutcome {
                code: Some(1),
                timed_out: false,
                output: "semantic analyzer unavailable".into(),
                elapsed: Duration::ZERO,
            };
        }
        let rules = self.slot_dir.join("semantic-rules.json");
        let policy = json!({"schema":"gamecore.stage.semantic-rules/1", "package":self.package(), "forbidEditorHooks":true,"forbidUnsafe":true,"forbidProcess":true,"forbidNetwork":true,"fileRoots":[format!("Assets/{}",self.package()),"persistentDataPath"]});
        if std::fs::write(&rules, policy.to_string()).is_err() {
            return ChildOutcome {
                code: Some(1),
                timed_out: false,
                output: "cannot write semantic rules".into(),
                elapsed: Duration::ZERO,
            };
        }
        cmd.arg("run")
            .arg("--project")
            .arg(target)
            .arg("--")
            .arg("--root")
            .arg(&self.slot_dir)
            .arg("--rules")
            .arg(rules)
            .arg("--out")
            .arg(&output);
        let mut result = self
            .opts
            .sandbox
            .run(&cmd, &self.scratch("semantic"), self.remaining());
        let findings = std::fs::read(&output)
            .ok()
            .and_then(|b| serde_json::from_slice::<Value>(&b).ok());
        if !findings.is_some_and(|v| {
            v["pass"] == true && v["findings"].as_array().is_some_and(Vec::is_empty)
        }) {
            result.code = Some(1);
        }
        result
    }

    fn unity(
        &mut self,
        label: &str,
        platform: &str,
        log: &mut StepLog,
    ) -> (ChildOutcome, Option<TestTotals>) {
        let results = self.out_dir().join(format!("{label}.xml"));
        let _ = std::fs::remove_file(&results);
        let attempt = self
            .opts
            .unity_attempt
            .min(self.remaining())
            .as_secs()
            .max(10);
        let out = self.opts.sandbox.run_unity(
            &self.opts.tools.unity_batch,
            &self.slot_dir.join("project"),
            label,
            &[
                "-runTests".into(),
                "-testPlatform".into(),
                platform.into(),
                "-testResults".into(),
                results.display().to_string(),
            ],
            Duration::from_secs(attempt),
        );
        log.child(&format!("unity-batch.sh {label} ({platform})"), &out);
        self.timed_out |= out.timed_out || out.code == Some(124);
        let totals = std::fs::read_to_string(&results)
            .ok()
            .and_then(|x| parse_nunit(&x));
        (out, totals)
    }

    fn compile_errors(output: &str) -> Vec<String> {
        let mut seen = BTreeSet::new();
        output
            .lines()
            .filter(|l| l.contains("error CS") || l.contains("Scripts have compiler errors"))
            .map(|l| l.trim().to_string())
            .filter(|l| seen.insert(l.clone()))
            .take(20)
            .collect()
    }

    // -- (d) ------------------------------------------------------------------------------
    fn step_editmode(&mut self) -> bool {
        let t = Instant::now();
        let mut log = StepLog::new();
        let (out, totals) = self.unity("editmode", "EditMode", &mut log);
        let errors = Self::compile_errors(&out.output);
        let delta: Option<CatalogDelta> = std::fs::read(self.out_dir().join("catalog-delta.json"))
            .ok()
            .and_then(|b| serde_json::from_slice(&b).ok());
        let integrity = integrity_problems(&self.package_dir(), &self.declared());
        for p in &integrity {
            log.line(format!("integrity: {p}"));
        }
        let mut facts = json!({
            "unityExit": out.code,
            "compileErrors": errors,
            "integrity": integrity.len(),
        });
        if let (Some(o), Some(tt)) = (facts.as_object_mut(), &totals) {
            o.insert("tests".into(), json!({"total": tt.total, "passed": tt.passed, "failed": tt.failed, "skipped": tt.skipped, "failures": tt.failures}));
        }
        let wants_catalog = self.record["harness"]["catalogType"]
            .as_str()
            .is_some_and(|c| !c.is_empty());
        let (status, detail) = if out.timed_out || out.code == Some(124) {
            (StepStatus::Fail, "the Unity Editor timed out".to_string())
        } else if !errors.is_empty() {
            (StepStatus::Fail, format!("compile failed: {}", errors[0]))
        } else if !integrity.is_empty() {
            (
                StepStatus::Fail,
                format!(
                    "the candidate package changed during the Unity run ({}); a package ships its .meta files",
                    integrity[0]
                ),
            )
        } else {
            match &totals {
                None => (
                    StepStatus::Fail,
                    format!("no EditMode test results (unity-batch exit {:?})", out.code),
                ),
                Some(tt) if tt.failed > 0 => (
                    StepStatus::Fail,
                    format!(
                        "{} EditMode test(s) failed: {}",
                        tt.failed,
                        tt.failures.first().map(String::as_str).unwrap_or("?")
                    ),
                ),
                Some(tt) if tt.total == 0 => (StepStatus::Fail, "no EditMode test ran".to_string()),
                Some(_)
                    if wants_catalog && delta.as_ref().is_none_or(|d| d.mechanisms.is_empty()) =>
                {
                    (
                        StepStatus::Fail,
                        "the catalog probe wrote no mechanism catalog".to_string(),
                    )
                }
                Some(tt) if out.ok() => (
                    StepStatus::Pass,
                    format!("compiled; {} EditMode test(s) passed", tt.passed),
                ),
                Some(_) => (
                    StepStatus::Fail,
                    format!("the Unity Editor exited {:?}", out.code),
                ),
            }
        };
        if let Some(d) = delta {
            self.delta = d;
        }
        let pass = status == StepStatus::Pass;
        self.finish(
            StepResult {
                facts,
                ..StepResult::new("unity-editmode", status, ms(t.elapsed()), detail)
            },
            log,
        );
        pass
    }

    // -- (e) + (f) ------------------------------------------------------------------------
    fn step_playmode(&mut self) -> Option<(Value, Value)> {
        let t = Instant::now();
        let mut log = StepLog::new();
        let smoke_type = self.record["harness"]["smokeType"]
            .as_str()
            .unwrap_or_default()
            .to_string();
        if smoke_type.is_empty() {
            self.finish(
                StepResult::new(
                    "playmode-smoke",
                    StepStatus::Fail,
                    0,
                    "the proposal declares no smokeTest entry; a mechanism must ship one",
                ),
                log,
            );
            return None;
        }
        for n in [1, 2] {
            let _ = std::fs::remove_file(self.out_dir().join(format!("smoke-{n}.json")));
        }
        let (out, totals) = self.unity("playmode", "PlayMode", &mut log);
        let read = |n: u32| -> Value {
            std::fs::read(self.out_dir().join(format!("smoke-{n}.json")))
                .ok()
                .and_then(|b| serde_json::from_slice(&b).ok())
                .unwrap_or(Value::Null)
        };
        let (s1, s2) = (read(1), read(2));
        let integrity = integrity_problems(&self.package_dir(), &self.declared());
        let errors = Self::compile_errors(&out.output);
        let mut facts = json!({"unityExit": out.code, "smoke": s1, "integrity": integrity.len()});
        if let (Some(o), Some(tt)) = (facts.as_object_mut(), &totals) {
            o.insert("tests".into(), json!({"total": tt.total, "passed": tt.passed, "failed": tt.failed, "skipped": tt.skipped, "failures": tt.failures}));
        }
        let smoke_ok = |s: &Value| {
            s["status"] == "ran"
                && s["errors"].as_u64() == Some(0)
                && s["duplicateFramePumps"].as_u64() == Some(0)
                && s["bypassPumps"].as_u64() == Some(0)
                && s["frames"].as_u64().is_some_and(|f| f > 0)
        };
        let (status, detail) = if out.timed_out || out.code == Some(124) {
            (StepStatus::Fail, "the Unity Editor timed out".to_string())
        } else if !errors.is_empty() {
            (StepStatus::Fail, format!("compile failed: {}", errors[0]))
        } else if !integrity.is_empty() {
            (
                StepStatus::Fail,
                format!(
                    "the candidate package changed during the Unity run ({})",
                    integrity[0]
                ),
            )
        } else if let Some(tt) = totals.as_ref().filter(|tt| tt.failed > 0) {
            (
                StepStatus::Fail,
                format!(
                    "{} PlayMode test(s) failed: {}",
                    tt.failed,
                    tt.failures.first().map(String::as_str).unwrap_or("?")
                ),
            )
        } else if s1.is_null() {
            (
                StepStatus::Fail,
                format!(
                    "the smoke runner wrote no result (unity-batch exit {:?})",
                    out.code
                ),
            )
        } else if !smoke_ok(&s1) {
            (
                StepStatus::Fail,
                format!(
                    "smoke: {} frame(s), {} sanctioned pump(s), {} duplicate, {} bypass, {} error(s){}",
                    s1["frames"],
                    s1["sanctionedPumps"],
                    s1["duplicateFramePumps"],
                    s1["bypassPumps"],
                    s1["errors"],
                    s1["firstError"]
                        .as_str()
                        .map(|e| format!(": {e}"))
                        .unwrap_or_default()
                ),
            )
        } else if !out.ok() {
            (
                StepStatus::Fail,
                format!("the Unity Editor exited {:?}", out.code),
            )
        } else {
            (
                StepStatus::Pass,
                format!(
                    "{} frame(s), {} sanctioned pump(s), no violation, no exception",
                    s1["frames"], s1["sanctionedPumps"]
                ),
            )
        };
        let ran = !s1.is_null();
        self.finish(
            StepResult {
                facts,
                ..StepResult::new("playmode-smoke", status, ms(t.elapsed()), detail)
            },
            log,
        );
        ran.then_some((s1, s2))
    }

    fn step_determinism(&mut self, smoke: Option<(Value, Value)>) {
        let Some((s1, s2)) = smoke else {
            self.skip("determinism", "skipped: the smoke runs did not run");
            return;
        };
        let h1 = s1["slotHash"].as_str().unwrap_or_default().to_string();
        let h2 = s2["slotHash"].as_str().unwrap_or_default().to_string();
        let hex = |h: &str| h.len() == 64 && h.chars().all(|c| c.is_ascii_hexdigit());
        let facts = json!({"slotHash1": h1, "slotHash2": h2});
        let (status, detail) = if !hex(&h1) || !hex(&h2) {
            (
                StepStatus::Fail,
                "a smoke run reported no canonical slot hash".to_string(),
            )
        } else if h1 != h2 {
            (
                StepStatus::Fail,
                format!(
                    "the two smoke runs diverged: {} != {}",
                    &h1[..12],
                    &h2[..12]
                ),
            )
        } else {
            (
                StepStatus::Pass,
                format!("both smoke runs end at slot hash {}", &h1[..12]),
            )
        };
        self.steps.push(StepResult {
            facts,
            ..StepResult::new("determinism", status, 0, detail)
        });
    }
}

fn ms(d: Duration) -> u64 {
    u64::try_from(d.as_millis()).unwrap_or(u64::MAX)
}

fn host_name() -> String {
    std::fs::read_to_string("/etc/hostname")
        .map(|s| s.trim().to_string())
        .ok()
        .filter(|s| !s.is_empty())
        .or_else(|| std::env::var("HOSTNAME").ok())
        .unwrap_or_default()
}

/// Make or refresh the slot from the options' source. Returns make-slot's summary.
pub fn prepare_slot(opts: &StageOptions, timeout: Duration) -> Result<Value, String> {
    let slot_dir = opts.slot_dir();
    let mut cmd = Command::new(&opts.tools.python);
    cmd.arg(&opts.tools.make_slot)
        .arg("--slot-root")
        .arg(&opts.root)
        .arg("--slot")
        .arg(&opts.slot)
        .arg("--source-project")
        .arg(&opts.source_project);
    match &opts.source {
        SlotSource::Existing => {
            return if slot_dir.join("stage.json").is_file() {
                Ok(json!({"ok": true, "slot": opts.slot, "reused": true}))
            } else {
                Err(format!(
                    "slot {} does not exist under {}; stage a candidate into it first",
                    opts.slot,
                    opts.root.display()
                ))
            };
        }
        SlotSource::Candidate(dir) => {
            cmd.arg("--candidate").arg(dir);
        }
        SlotSource::PackageDir { dir, change_set_id } => {
            cmd.arg("--package-dir")
                .arg(dir)
                .arg("--change-set-id")
                .arg(change_set_id);
        }
    }
    let warm = opts.sandbox.cache.join("Library");
    if warm.is_dir() {
        cmd.arg("--warm-library").arg(&warm);
    }
    if opts.force {
        cmd.arg("--force");
    }
    std::fs::create_dir_all(&slot_dir).map_err(|e| format!("cannot create the slot: {e}"))?;
    let out = run_child(&mut cmd, &[], &slot_dir.join(".make-slot.raw"), timeout);
    let summary = out.last_json().unwrap_or(Value::Null);
    if out.ok() && summary["ok"] == true {
        Ok(summary)
    } else if out.timed_out {
        Err("making the slot did not finish within the stage budget".into())
    } else {
        Err(summary["error"]
            .as_str()
            .map(str::to_string)
            .unwrap_or_else(|| {
                let tail: String = out.output.chars().rev().take(600).collect();
                format!(
                    "make-slot.py failed (exit {:?}): {}",
                    out.code,
                    tail.chars().rev().collect::<String>()
                )
            }))
    }
}

/// Cache identity changes automatically with the Unity version and package version set.
pub fn cache_version(opts: &StageOptions) -> Result<String, String> {
    let mut inputs = std::fs::read(
        opts.source_project
            .join("ProjectSettings/ProjectVersion.txt"),
    )
    .map_err(|e| e.to_string())?;
    let mut packages = std::fs::read_dir(opts.repo.join("Packages"))
        .map_err(|e| e.to_string())?
        .filter_map(Result::ok)
        .map(|e| e.path())
        .collect::<Vec<_>>();
    packages.sort();
    for path in packages {
        let name = path.file_name().unwrap_or_default().to_string_lossy();
        if path.is_dir()
            && name.starts_with("com.gamecore.")
            && !name.starts_with("com.gamecore.studio.")
        {
            let bytes = std::fs::read(path.join("package.json")).map_err(|e| e.to_string())?;
            inputs.extend_from_slice(name.as_bytes());
            inputs.extend(bytes);
        }
    }
    Ok(sha256_hex(&inputs))
}

/// Seed the warm Library cache from a slot that compiled (first successful stage only).
fn seed_warm_library(root: &Path, project: &Path) {
    let Ok(_cache_lock) = SlotLock::acquire(root, slot::MAX_SLOT_AGE) else {
        return;
    };
    let warm = root.to_path_buf();
    let target = warm.join("Library");
    let library = project.join("Library");
    if target.exists() || !library.is_dir() || std::fs::create_dir_all(&warm).is_err() {
        return;
    }
    let tmp = warm.join(format!("Library.{}.tmp", std::process::id()));
    let ok = Command::new("/bin/cp")
        .env_clear()
        .envs(stage_env())
        .arg("-a")
        .arg(&library)
        .arg(&tmp)
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .is_ok_and(|s| s.success());
    if ok {
        let _ = std::fs::rename(&tmp, &target);
    } else {
        let _ = std::fs::remove_dir_all(&tmp);
    }
}

/// Cold-cache grace can be consumed once per cache version, even if that attempt fails.
fn cold_budget(cache: &Path, warm: Duration) -> Result<(bool, Duration), String> {
    std::fs::create_dir_all(cache).map_err(|e| e.to_string())?;
    let cold = !cache.join("Library").is_dir();
    let first = cold
        && std::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(cache.join(".cold-grace-used"))
            .is_ok();
    Ok((
        cold,
        if first {
            warm.max(Duration::from_secs(1800))
        } else {
            warm
        },
    ))
}

/// Run one stage. `Err` only when no verdict can be made (bad slot, slot busy, the candidate
/// cannot be staged); every step outcome, timeouts included, is a verdict.
pub fn run_stage(opts: &StageOptions) -> Result<StageVerdict, String> {
    let mut effective = opts.clone();
    effective.sandbox.slot = effective.slot_dir();
    effective.sandbox.cache = effective
        .root
        .join("_warm")
        .join(cache_version(&effective)?);
    let (cold_cache, budget) = cold_budget(&effective.sandbox.cache, effective.budget)?;
    effective.budget = budget;
    let opts = &effective;
    let started = Instant::now();
    if !slot::valid_slot_id(&opts.slot) {
        return Err(format!("{:?} is not a slot id", opts.slot));
    }
    let slot_dir = opts.slot_dir();
    let _lock = SlotLock::acquire(&slot_dir, opts.budget.saturating_mul(4))?;
    // Probe before any candidate compiler is allowed to start. No fallback on failure.
    opts.sandbox.probe()?;
    prepare_slot(opts, opts.budget)?;
    let record: Value = std::fs::read(slot_dir.join("stage.json"))
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .ok_or("the slot has no readable stage.json")?;
    if opts
        .expected_source_revision
        .as_ref()
        .is_some_and(|revision| record["source"]["commit"].as_str() != Some(revision.as_str()))
    {
        return Err("source revision changed while preparing the stage slot".into());
    }
    if let SlotSource::PackageDir { change_set_id, .. } = &opts.source
        && record["changeSetId"].as_str() != Some(change_set_id.as_str())
    {
        return Err("the slot holds another change set".into());
    }
    std::fs::create_dir_all(slot_dir.join("out").join("logs"))
        .map_err(|e| format!("cannot create the slot's out directory: {e}"))?;
    let mut run = Run {
        opts,
        slot_dir: slot_dir.clone(),
        record,
        deadline: started + opts.budget,
        steps: Vec::new(),
        timed_out: false,
        hits: Vec::new(),
        delta: CatalogDelta::default(),
    };

    // Code runs only after the scan and the checkers passed.
    let mut blocked: Option<String> = None;
    for id in ["scan", "checkers"] {
        if !run.requested(id) {
            run.skip(id, "not requested");
            blocked.get_or_insert_with(|| format!("mandatory prefilter {id} did not run"));
            continue;
        }
        if run.budget_gone(id) {
            blocked.get_or_insert_with(|| "the stage budget was exhausted".into());
            continue;
        }
        if id == "scan" {
            run.step_scan();
        } else {
            run.step_checkers();
        }
        if run
            .steps
            .last()
            .is_some_and(|s| s.status == StepStatus::Fail)
        {
            blocked.get_or_insert_with(|| format!("skipped: {id} failed (no candidate code runs)"));
        }
    }
    let gate = |run: &mut Run<'_>, id: &str, blocked: &Option<String>| -> bool {
        if let Some(why) = blocked {
            run.skip(id, why);
            return false;
        }
        if !run.requested(id) {
            run.skip(id, "not requested");
            return false;
        }
        !run.budget_gone(id)
    };
    if gate(&mut run, "dotnet", &blocked) {
        run.step_dotnet();
        if run
            .steps
            .last()
            .is_some_and(|s| s.status == StepStatus::Fail)
        {
            blocked = Some("skipped: dotnet failed".into());
        }
    } else if run.timed_out {
        blocked.get_or_insert_with(|| "the stage budget was exhausted".into());
    }
    let mut editmode_ok = false;
    if gate(&mut run, "unity-editmode", &blocked) {
        editmode_ok = run.step_editmode();
        if !editmode_ok {
            blocked = Some("skipped: unity-editmode failed".into());
        }
    }
    let mut smoke = None;
    if gate(&mut run, "playmode-smoke", &blocked) {
        smoke = run.step_playmode();
        if run
            .steps
            .last()
            .is_some_and(|s| s.status == StepStatus::Fail)
            && smoke.is_none()
        {
            blocked = Some("skipped: playmode-smoke failed".into());
        }
    }
    if let Some(why) = &blocked
        && smoke.is_none()
    {
        run.skip("determinism", why);
    } else if !run.requested("determinism") {
        run.skip("determinism", "not requested");
    } else {
        run.step_determinism(smoke);
    }

    let elapsed = started.elapsed();
    let over = elapsed > opts.budget;
    let budget_ok = !over && !run.timed_out;
    run.steps.push(StepResult {
        facts: json!({"elapsedMs": ms(elapsed), "budgetMs": ms(opts.budget)}),
        ..StepResult::new(
            "budget",
            if budget_ok {
                StepStatus::Pass
            } else {
                StepStatus::Fail
            },
            0,
            if budget_ok {
                format!(
                    "{} s within B-STAGE {} s",
                    elapsed.as_secs(),
                    opts.budget.as_secs()
                )
            } else {
                format!(
                    "stage_failed{{timeout}}: {} s, B-STAGE {} s",
                    elapsed.as_secs(),
                    opts.budget.as_secs()
                )
            },
        )
    });
    // Keep the report order stable whatever happened.
    run.steps.sort_by_key(|s| {
        STEP_IDS
            .iter()
            .position(|id| *id == s.id)
            .unwrap_or(usize::MAX)
    });

    let mut artifacts = Vec::new();
    for (role, key) in [("package", "artifact"), ("proposal", "proposal")] {
        if let Some(sha) = run.record["package"][key].as_str() {
            artifacts.push(CoveredArtifact {
                role: role.into(),
                sha256: sha.into(),
            });
        }
    }
    let mut verdict = StageVerdict {
        confinement: opts.sandbox.mode.name().into(),
        cold_cache,
        schema: VERDICT_SCHEMA.into(),
        change_set_id: run.record["changeSetId"]
            .as_str()
            .unwrap_or_default()
            .into(),
        slot: opts.slot.clone(),
        package: run.package(),
        steps: std::mem::take(&mut run.steps),
        pass: false,
        partial: opts.steps.is_some(),
        artifacts,
        files: run.declared(),
        catalog_delta: std::mem::take(&mut run.delta),
        forbidden_hits: std::mem::take(&mut run.hits),
        failure: (!budget_ok).then(|| "timeout".to_string()),
        runner: RunnerInfo {
            version: crate::VERSION.into(),
            host: host_name(),
            source_commit: run.record["source"]["commit"].as_str().map(str::to_string),
            template: run.record["template"].as_str().map(str::to_string),
        },
        duration_ms: ms(elapsed),
        budget_ms: ms(opts.budget),
        created_at: now_ms(),
    };
    verdict.settle();
    let _ = std::fs::write(slot_dir.join("out").join("verdict.json"), verdict.bytes());
    if editmode_ok {
        seed_warm_library(&opts.sandbox.cache, &slot_dir.join("project"));
    }
    Ok(verdict)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn r2_11_cache_versions_and_one_cold_budget() {
        let dir = tempfile::tempdir().unwrap();
        let repo = dir.path();
        std::fs::create_dir_all(repo.join("Packages/com.gamecore.contracts")).unwrap();
        std::fs::create_dir_all(repo.join("project/ProjectSettings")).unwrap();
        let manifest = repo.join("Packages/com.gamecore.contracts/package.json");
        let unity = repo.join("project/ProjectSettings/ProjectVersion.txt");
        std::fs::write(&manifest, r#"{"version":"1"}"#).unwrap();
        std::fs::write(&unity, "6000.0.75f1").unwrap();
        std::fs::write(repo.join("Packages/com.gamecore.contracts.meta"), "meta").unwrap();
        let mut opts = StageOptions::from_env(repo, "test", SlotSource::Existing);
        opts.source_project = repo.join("project");
        let first = cache_version(&opts).unwrap();
        std::fs::write(&manifest, r#"{"version":"2"}"#).unwrap();
        assert_ne!(first, cache_version(&opts).unwrap());
        let second = cache_version(&opts).unwrap();
        std::fs::write(&unity, "6000.1").unwrap();
        assert_ne!(second, cache_version(&opts).unwrap());
        let cache = repo.join("cache");
        let warm = Duration::from_secs(360);
        assert_eq!(
            cold_budget(&cache, warm).unwrap(),
            (true, Duration::from_secs(1800))
        );
        assert_eq!(cold_budget(&cache, warm).unwrap(), (true, warm));
        std::fs::create_dir(cache.join("Library")).unwrap();
        assert_eq!(cold_budget(&cache, warm).unwrap(), (false, warm));
    }

    #[test]
    fn nunit_totals_and_failures() {
        let xml = r#"<?xml version="1.0"?>
<test-run id="2" testcasecount="3" result="Failed(Child)" total="3" passed="2" failed="1" inconclusive="0" skipped="0">
  <test-suite type="Assembly" name="X">
    <test-case id="1" name="Presses" fullname="Plate.Tests.Presses" result="Passed" />
    <test-case id="2" name="Breaks" fullname="Plate.Tests.Breaks &quot;on purpose&quot;" result="Failed">
      <failure><message>boom</message></failure>
    </test-case>
    <test-case id="3" name="Other" fullname="Plate.Tests.Other" result="Passed"/>
  </test-suite>
</test-run>"#;
        let t = parse_nunit(xml).unwrap();
        assert_eq!((t.total, t.passed, t.failed, t.skipped), (3, 2, 1, 0));
        assert_eq!(
            t.failures,
            vec!["Plate.Tests.Breaks \"on purpose\"".to_string()]
        );
        assert!(parse_nunit("<nothing/>").is_none());
    }

    #[test]
    fn dotnet_summary() {
        let out = "Build succeeded.\nPassed!  - Failed:     0, Passed:     7, Skipped:     1, Total:     8, Duration: 41 ms - Rules.Tests.dll (net8.0)\n";
        let t = parse_dotnet_summary(out).unwrap();
        assert_eq!((t.total, t.passed, t.failed, t.skipped), (8, 7, 0, 1));
        assert!(parse_dotnet_summary("nothing").is_none());
    }

    #[test]
    fn integrity_finds_missing_changed_and_extra_files() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path();
        std::fs::create_dir_all(p.join("Runtime")).unwrap();
        std::fs::write(p.join("package.json"), "a").unwrap();
        std::fs::write(p.join("Runtime/A.cs"), "b").unwrap();
        let declared = vec![
            PackageFile {
                path: "package.json".into(),
                sha256: sha256_hex(b"a"),
                bytes: 1,
            },
            PackageFile {
                path: "Runtime/A.cs".into(),
                sha256: sha256_hex(b"b"),
                bytes: 1,
            },
        ];
        assert!(integrity_problems(p, &declared).is_empty());
        std::fs::write(p.join("Runtime/A.cs.meta"), "m").unwrap();
        std::fs::write(p.join("package.json"), "x").unwrap();
        std::fs::remove_file(p.join("Runtime/A.cs")).unwrap();
        assert_eq!(
            integrity_problems(p, &declared),
            vec![
                "Runtime/A.cs is missing",
                "Runtime/A.cs.meta appeared",
                "package.json changed"
            ]
        );
    }

    #[test]
    fn r2_19_child_log_is_redacted_before_process_exit() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("stream.log");
        let capture = path.clone();
        let child = std::thread::spawn(move || {
            run_child(
                Command::new("sh").args(["-c", "printf etk_; sleep 0.1; echo abcdefgh; sleep 0.5"]),
                &[],
                &capture,
                Duration::from_secs(3),
            )
        });
        let deadline = Instant::now() + Duration::from_secs(2);
        let mut observed = false;
        while Instant::now() < deadline && !child.is_finished() {
            let bytes = std::fs::read_to_string(&path).unwrap_or_default();
            assert!(!bytes.contains("abcdefgh"));
            if bytes.contains("[redacted]") {
                observed = true;
                break;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        assert!(
            observed,
            "redacted durable output must exist before the child exits"
        );
        assert!(child.join().unwrap().ok());
    }

    #[test]
    fn a_child_gets_the_allowlisted_env_and_is_killed_at_its_deadline() {
        let dir = tempfile::tempdir().unwrap();
        let out = run_child(
            Command::new("sh")
                .arg("-c")
                .arg("env; echo token etk_abcdefghijkl; echo key sk-ABCDEFGHIJKLMNOPQRSTUV"),
            &[("GAMECORE_STAGE_MARK", "1")],
            &dir.path().join("a.raw"),
            Duration::from_secs(20),
        );
        assert!(out.ok(), "{out:?}");
        assert!(out.output.contains("GAMECORE_STAGE_MARK=1"));
        for line in out.output.lines() {
            if let Some((name, _)) = line.split_once('=')
                && !name.contains(' ')
            {
                assert!(
                    super::super::env::allowed(name)
                        || name == "GAMECORE_STAGE_MARK"
                        || name == "PATH"
                        || name == "PWD"
                        || name == "SHLVL"
                        || name == "_",
                    "{name} leaked"
                );
            }
        }
        assert!(!out.output.contains("abcdefghijkl"));
        assert!(!out.output.contains("ABCDEFGHIJKLMNOPQRSTUV"));
        assert!(!dir.path().join("a.raw").exists());

        let t = Instant::now();
        let out = run_child(
            Command::new("sh")
                .arg("-c")
                .arg("sleep 30 & sleep 30; echo never"),
            &[],
            &dir.path().join("b.raw"),
            Duration::from_millis(500),
        );
        assert!(out.timed_out);
        assert!(!out.ok());
        assert!(t.elapsed() < Duration::from_secs(20));
        assert!(!out.output.contains("never"));
    }
}
