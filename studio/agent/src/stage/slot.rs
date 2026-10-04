//! Staging slots on disk: where they live, how they are named, the one-stage-per-slot lock,
//! the one-slot-per-change-set rule and garbage collection (older than seven days, or on
//! Reject/discard).
//!
//! A slot is `<root>/<slot-id>/` (made by `studio/stage/make-slot.py`; layout in
//! `studio/stage/README.md`). `<root>` is `GAMECORE_STAGE_ROOT`, else
//! `$XDG_CACHE_HOME/gamecore-studio/stage`, else `~/.cache/gamecore-studio/stage`. Names that
//! start with `_` or `.` are not slots (`_warm/Library` is the warm import cache).

use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime};

use serde_json::Value;

/// Slots older than this are collected.
pub const MAX_SLOT_AGE: Duration = Duration::from_secs(7 * 24 * 3600);

/// The lock file of a running stage.
pub const LOCK_NAME: &str = ".stage.lock";

/// The default slot root.
pub fn default_root() -> PathBuf {
    if let Some(v) = std::env::var_os("GAMECORE_STAGE_ROOT").filter(|v| !v.is_empty()) {
        return PathBuf::from(v);
    }
    if let Some(v) = std::env::var_os("XDG_CACHE_HOME").filter(|v| !v.is_empty()) {
        return PathBuf::from(v).join("gamecore-studio").join("stage");
    }
    let home = std::env::var_os("HOME").map(PathBuf::from).unwrap_or_else(std::env::temp_dir);
    home.join(".cache").join("gamecore-studio").join("stage")
}

/// True for a slot id: 1-64 characters of `[a-z0-9._-]`, starting with a letter or digit.
pub fn valid_slot_id(id: &str) -> bool {
    let mut chars = id.chars();
    matches!(chars.next(), Some(c) if c.is_ascii_lowercase() || c.is_ascii_digit())
        && id.len() <= 64
        && chars.all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || matches!(c, '.' | '_' | '-'))
}

/// The default slot of a change set: `cs-<ulid in lower case>`.
pub fn default_slot_id(change_set_id: &str) -> String {
    let ulid = change_set_id.strip_prefix("cs_").unwrap_or(change_set_id);
    format!("cs-{}", ulid.to_ascii_lowercase())
}

/// The change set a slot holds (from its `stage.json`).
pub fn slot_change_set(slot_dir: &Path) -> Option<String> {
    let text = std::fs::read_to_string(slot_dir.join("stage.json")).ok()?;
    let v: Value = serde_json::from_str(&text).ok()?;
    v.get("changeSetId").and_then(Value::as_str).map(str::to_string)
}

/// Slot directories under `root` (sorted).
pub fn slots(root: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(root) else {
        return Vec::new();
    };
    let mut out: Vec<PathBuf> = entries
        .filter_map(Result::ok)
        .filter(|e| e.file_type().is_ok_and(|t| t.is_dir()))
        .filter(|e| {
            let n = e.file_name().to_string_lossy().into_owned();
            !n.starts_with('_') && !n.starts_with('.') && valid_slot_id(&n)
        })
        .map(|e| e.path())
        .collect();
    out.sort();
    out
}

/// The slot for a change set: the one already holding it, else `requested`, else the default.
/// One slot per change set: asking for a different slot than the one holding it is refused,
/// and so is a slot holding another change set.
pub fn resolve_slot(root: &Path, change_set_id: &str, requested: Option<&str>) -> Result<String, String> {
    if let Some(r) = requested
        && !valid_slot_id(r)
    {
        return Err(format!("slot {r:?} is not a slot id ([a-z0-9._-], at most 64)"));
    }
    let holding = slots(root)
        .into_iter()
        .find(|d| slot_change_set(d).as_deref() == Some(change_set_id))
        .and_then(|d| d.file_name().map(|n| n.to_string_lossy().into_owned()));
    match (holding, requested) {
        (Some(h), Some(r)) if h != r => Err(format!(
            "change set {change_set_id} is staged in slot {h}; one slot per change set"
        )),
        (Some(h), _) => Ok(h),
        (None, Some(r)) => match slot_change_set(&root.join(r)) {
            Some(other) if other != change_set_id => Err(format!(
                "slot {r} holds change set {other}; one change set per slot"
            )),
            _ => Ok(r.to_string()),
        },
        (None, None) => Ok(default_slot_id(change_set_id)),
    }
}

/// The exclusive lock of one stage on one slot (removed on drop).
#[derive(Debug)]
pub struct SlotLock {
    path: PathBuf,
}

impl SlotLock {
    /// Take the lock of `slot_dir` (creating the directory). A lock left by a process that no
    /// longer runs (Linux: no `/proc/<pid>`; elsewhere: older than `stale_after`) is replaced.
    pub fn acquire(slot_dir: &Path, stale_after: Duration) -> Result<SlotLock, String> {
        std::fs::create_dir_all(slot_dir).map_err(|e| format!("cannot create {}: {e}", slot_dir.display()))?;
        let path = slot_dir.join(LOCK_NAME);
        for _ in 0..2 {
            match std::fs::OpenOptions::new().write(true).create_new(true).open(&path) {
                Ok(mut f) => {
                    use std::io::Write;
                    let _ = writeln!(f, "{}", std::process::id());
                    return Ok(SlotLock { path });
                }
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {
                    if lock_is_stale(&path, stale_after) {
                        let _ = std::fs::remove_file(&path);
                        continue;
                    }
                    let holder = std::fs::read_to_string(&path).unwrap_or_default();
                    return Err(format!(
                        "slot {} is being staged (pid {}); one stage per slot",
                        slot_dir.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
                        holder.trim()
                    ));
                }
                Err(e) => return Err(format!("cannot lock {}: {e}", path.display())),
            }
        }
        Err(format!("cannot lock {}", path.display()))
    }
}

impl Drop for SlotLock {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

fn lock_is_stale(path: &Path, stale_after: Duration) -> bool {
    let pid = std::fs::read_to_string(path)
        .ok()
        .and_then(|t| t.trim().parse::<u32>().ok());
    if Path::new("/proc/self").is_dir() {
        return match pid {
            Some(p) => !Path::new(&format!("/proc/{p}")).exists(),
            None => true,
        };
    }
    age(path).is_some_and(|a| a > stale_after)
}

/// True when a stage holds the slot's lock.
pub fn is_locked(slot_dir: &Path) -> bool {
    let path = slot_dir.join(LOCK_NAME);
    path.exists() && !lock_is_stale(&path, MAX_SLOT_AGE)
}

fn age(path: &Path) -> Option<Duration> {
    let modified = std::fs::metadata(path).and_then(|m| m.modified()).ok()?;
    SystemTime::now().duration_since(modified).ok()
}

/// The age of a slot: since its newest of `stage.json` / `out/verdict.json` (else the directory).
pub fn slot_age(slot_dir: &Path) -> Option<Duration> {
    [slot_dir.join("stage.json"), slot_dir.join("out").join("verdict.json"), slot_dir.to_path_buf()]
        .iter()
        .filter_map(|p| age(p))
        .min()
}

/// Remove a slot directory (read-only stage inputs included).
pub fn remove_slot(slot_dir: &Path) -> std::io::Result<()> {
    if !slot_dir.exists() {
        return Ok(());
    }
    make_writable(slot_dir);
    std::fs::remove_dir_all(slot_dir)
}

fn make_writable(dir: &Path) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for e in entries.filter_map(Result::ok) {
        let p = e.path();
        if let Ok(t) = e.file_type() {
            if t.is_dir() {
                set_owner_writable(&p);
                make_writable(&p);
            } else if t.is_file() {
                set_owner_writable(&p);
            }
        }
    }
}

fn set_owner_writable(p: &Path) {
    use std::os::unix::fs::PermissionsExt;
    if let Ok(m) = std::fs::metadata(p) {
        let mut perm = m.permissions();
        perm.set_mode(perm.mode() | 0o200);
        let _ = std::fs::set_permissions(p, perm);
    }
}

/// Collect slots older than `max_age` that no stage holds. Returns the removed slot ids.
pub fn gc(root: &Path, max_age: Duration) -> Vec<String> {
    let mut removed = Vec::new();
    for dir in slots(root) {
        if is_locked(&dir) {
            continue;
        }
        if slot_age(&dir).is_some_and(|a| a > max_age) && remove_slot(&dir).is_ok() {
            removed.push(dir.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default());
        }
    }
    removed
}

#[cfg(test)]
mod tests {
    use super::*;

    const CS: &str = "cs_01JAPP0000000000000000PLAT";

    fn make(root: &Path, slot: &str, cs: &str) -> PathBuf {
        let d = root.join(slot);
        std::fs::create_dir_all(d.join("project")).unwrap();
        std::fs::write(d.join("stage.json"), format!("{{\"changeSetId\":\"{cs}\"}}")).unwrap();
        d
    }

    fn backdate(path: &Path, by: Duration) {
        let f = std::fs::File::open(path).unwrap();
        f.set_modified(SystemTime::now() - by).unwrap();
    }

    #[test]
    fn slot_ids_and_defaults() {
        assert!(valid_slot_id("cs-01japp0000000000000000plat"));
        assert!(valid_slot_id("1"));
        assert!(!valid_slot_id(""));
        assert!(!valid_slot_id("-a"));
        assert!(!valid_slot_id("A"));
        assert!(!valid_slot_id("a/b"));
        assert!(!valid_slot_id(&"a".repeat(65)));
        assert_eq!(default_slot_id(CS), "cs-01japp0000000000000000plat");
        assert!(valid_slot_id(&default_slot_id(CS)));
    }

    #[test]
    fn one_slot_per_change_set_and_one_change_set_per_slot() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path();
        assert_eq!(resolve_slot(root, CS, None).unwrap(), default_slot_id(CS));
        make(root, "s1", CS);
        assert_eq!(resolve_slot(root, CS, None).unwrap(), "s1");
        assert_eq!(resolve_slot(root, CS, Some("s1")).unwrap(), "s1");
        assert!(resolve_slot(root, CS, Some("s2")).unwrap_err().contains("one slot per change set"));
        let other = "cs_01JAPP0000000000000000NEG1";
        assert!(resolve_slot(root, other, Some("s1")).unwrap_err().contains("one change set per slot"));
        assert_eq!(resolve_slot(root, other, Some("s3")).unwrap(), "s3");
        assert!(resolve_slot(root, other, Some("../x")).is_err());
    }

    #[test]
    fn one_stage_per_slot() {
        let dir = tempfile::tempdir().unwrap();
        let slot = dir.path().join("s1");
        let lock = SlotLock::acquire(&slot, MAX_SLOT_AGE).unwrap();
        assert!(is_locked(&slot));
        let err = SlotLock::acquire(&slot, MAX_SLOT_AGE).unwrap_err();
        assert!(err.contains("one stage per slot"), "{err}");
        drop(lock);
        assert!(!is_locked(&slot));
        let again = SlotLock::acquire(&slot, MAX_SLOT_AGE);
        assert!(again.is_ok());
    }

    #[test]
    fn a_lock_of_a_dead_process_is_replaced() {
        let dir = tempfile::tempdir().unwrap();
        let slot = dir.path().join("s1");
        std::fs::create_dir_all(&slot).unwrap();
        std::fs::write(slot.join(LOCK_NAME), "4294967294\n").unwrap();
        backdate(&slot.join(LOCK_NAME), Duration::from_secs(3600));
        assert!(SlotLock::acquire(&slot, Duration::from_secs(60)).is_ok());
    }

    #[test]
    fn gc_removes_old_unlocked_slots_only() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path();
        let old = make(root, "old", CS);
        let fresh = make(root, "fresh", "cs_01JAPP0000000000000000NEG1");
        let busy = make(root, "busy", "cs_01JAPP0000000000000000NEG2");
        std::fs::create_dir_all(root.join("_warm").join("Library")).unwrap();
        // A read-only stage input inside the old slot does not stop its removal.
        let input = old.join("project").join("W.asset");
        std::fs::write(&input, "w").unwrap();
        let mut perm = std::fs::metadata(&input).unwrap().permissions();
        std::os::unix::fs::PermissionsExt::set_mode(&mut perm, 0o444);
        std::fs::set_permissions(&input, perm).unwrap();
        let eight_days = Duration::from_secs(8 * 24 * 3600);
        for d in [&old, &busy] {
            backdate(&d.join("stage.json"), eight_days);
            backdate(d, eight_days);
        }
        let _lock = SlotLock::acquire(&busy, MAX_SLOT_AGE).unwrap();
        backdate(&busy.join("stage.json"), eight_days);
        backdate(&busy, eight_days);
        let removed = gc(root, MAX_SLOT_AGE);
        assert_eq!(removed, vec!["old".to_string()]);
        assert!(!old.exists());
        assert!(fresh.exists());
        assert!(busy.exists());
        assert!(root.join("_warm").exists());
    }

    #[test]
    fn a_recent_verdict_keeps_a_slot() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path();
        let slot = make(root, "s1", CS);
        std::fs::create_dir_all(slot.join("out")).unwrap();
        std::fs::write(slot.join("out").join("verdict.json"), "{}").unwrap();
        backdate(&slot.join("stage.json"), Duration::from_secs(8 * 24 * 3600));
        backdate(&slot, Duration::from_secs(8 * 24 * 3600));
        assert!(gc(root, MAX_SLOT_AGE).is_empty());
    }
}
