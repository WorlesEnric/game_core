//! The durable ledger (`ETOS_STATE_DIR/ledger.db`, SQLite, WAL, `synchronous = FULL`).
//!
//! | Table | Holds |
//! |---|---|
//! | `requests` | one row per change set: body digest (idempotency), state, etos task status, attempt, outcome, `seq` |
//! | `attempts` | one row per etos task opened for a request: etos request id, topic, task id, parent task, topic cursor, uploaded inputs |
//! | `candidates` | the validated change set and its artifact manifest |
//! | `artifacts` | content-addressed files: digest, path, size, media type, producer |
//! | `events` | the monotonic event log behind `WS /v1/events` (cursor = row id) |
//! | `voice_sessions`, `stage_jobs`, `tool_catalogs`, `meta` | as named |
//!
//! Every request change appends a `request` event in the same transaction and stores that
//! event's cursor as the request's `seq`, so `GET /v1/requests?after=<seq>` and
//! `WS /v1/events?after=<cursor>` share one ordering.

use std::path::Path;
use std::sync::{Mutex, MutexGuard};

use rusqlite::{Connection, OptionalExtension, Transaction, params};
use serde_json::Value;

use crate::model::{EventView, RequestState, RequestView, StageJobView};
use crate::util::now_ms;

/// A ledger failure.
#[derive(Debug, thiserror::Error)]
pub enum LedgerError {
    /// SQLite failed.
    #[error("sqlite: {0}")]
    Sqlite(#[from] rusqlite::Error),
    /// A stored JSON value is malformed.
    #[error("json: {0}")]
    Json(#[from] serde_json::Error),
    /// No such row.
    #[error("not found: {0}")]
    NotFound(String),
    /// A different row already exists under the same key.
    #[error("conflict: {0}")]
    Conflict(String),
    /// The connection is unusable after a panic.
    #[error("the ledger lock is poisoned")]
    Poisoned,
}

/// Result of a ledger call.
pub type LedgerResult<T> = Result<T, LedgerError>;

const SCHEMA: &str = r#"
CREATE TABLE IF NOT EXISTS requests (
  request_id    TEXT PRIMARY KEY,
  change_set_id TEXT NOT NULL UNIQUE,
  digest        TEXT NOT NULL,
  body          TEXT NOT NULL,
  app           TEXT NOT NULL,
  worker        TEXT NOT NULL,
  state         TEXT NOT NULL,
  task_status   TEXT,
  attempt       INTEGER NOT NULL DEFAULT 0,
  outcome       TEXT,
  seq           INTEGER NOT NULL DEFAULT 0,
  created_at    INTEGER NOT NULL,
  updated_at    INTEGER NOT NULL,
  resumes       INTEGER NOT NULL DEFAULT 0,
  given_up      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS requests_seq ON requests(seq);
CREATE TABLE IF NOT EXISTS attempts (
  request_id      TEXT NOT NULL,
  attempt         INTEGER NOT NULL,
  etos_request_id TEXT NOT NULL,
  topic           TEXT NOT NULL,
  task_id         TEXT,
  parent_task     TEXT,
  cursor          INTEGER NOT NULL DEFAULT 0,
  inputs          TEXT,
  created_at      INTEGER NOT NULL,
  PRIMARY KEY (request_id, attempt)
);
CREATE TABLE IF NOT EXISTS candidates (
  change_set_id TEXT PRIMARY KEY,
  request_id    TEXT NOT NULL,
  task_id       TEXT NOT NULL,
  attempt       INTEGER NOT NULL,
  change_set    TEXT NOT NULL,
  artifacts     TEXT NOT NULL,
  diagnostics   TEXT NOT NULL,
  received_at   INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS artifacts (
  sha256        TEXT PRIMARY KEY,
  path          TEXT NOT NULL,
  bytes         INTEGER NOT NULL,
  media_type    TEXT NOT NULL,
  name          TEXT NOT NULL,
  role          TEXT,
  producer      TEXT NOT NULL,
  change_set_id TEXT,
  created_at    INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS events (
  cursor     INTEGER PRIMARY KEY AUTOINCREMENT,
  at         INTEGER NOT NULL,
  kind       TEXT NOT NULL,
  request_id TEXT,
  data       TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS voice_sessions (
  session_id  TEXT PRIMARY KEY,
  app         TEXT NOT NULL,
  provider    TEXT NOT NULL,
  started_at  INTEGER NOT NULL,
  ended_at    INTEGER,
  reason      TEXT,
  chunks      INTEGER NOT NULL DEFAULT 0,
  audio_bytes INTEGER NOT NULL DEFAULT 0,
  transcripts INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS stage_jobs (
  job_id        TEXT PRIMARY KEY,
  change_set_id TEXT NOT NULL,
  package_ref   TEXT NOT NULL,
  state         TEXT NOT NULL,
  slot          TEXT,
  verdict       TEXT,
  created_at    INTEGER NOT NULL,
  updated_at    INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS tool_catalogs (
  revision   TEXT PRIMARY KEY,
  digest     TEXT NOT NULL,
  catalog    TEXT NOT NULL,
  created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
"#;

/// A request row.
#[derive(Debug, Clone, PartialEq)]
pub struct RequestRow {
    /// Request id (= change-set id).
    pub request_id: String,
    /// Change-set id.
    pub change_set_id: String,
    /// Canonical digest of the request body.
    pub digest: String,
    /// The request body as received.
    pub body: Value,
    /// The calling app.
    pub app: String,
    /// The worker.
    pub worker: String,
    /// State.
    pub state: RequestState,
    /// etos task status, verbatim.
    pub task_status: Option<String>,
    /// Current attempt.
    pub attempt: u32,
    /// Outcome.
    pub outcome: Option<Value>,
    /// Ledger position of the last change.
    pub seq: i64,
    /// Created (ms).
    pub created_at: i64,
    /// Updated (ms).
    pub updated_at: i64,
}

/// One etos task opened for a request.
#[derive(Debug, Clone, PartialEq)]
pub struct AttemptRow {
    /// The request.
    pub request_id: String,
    /// 0, then 1 for the re-ask.
    pub attempt: u32,
    /// The `id` sent with `POST /tasks` (idempotency key at etos).
    pub etos_request_id: String,
    /// The origin topic `#agent/<agent>/<name>`.
    pub topic: String,
    /// The task id, once open.
    pub task_id: Option<String>,
    /// The task this one re-asks.
    pub parent_task: Option<String>,
    /// Last topic position processed.
    pub cursor: u64,
    /// Uploaded inputs `[{name, ref, digest}]`, once packed.
    pub inputs: Option<Value>,
    /// Created (ms).
    pub created_at: i64,
}

/// A new request.
#[derive(Debug, Clone)]
pub struct NewRequest {
    /// Change-set id (also the request id).
    pub change_set_id: String,
    /// Canonical digest of the body.
    pub digest: String,
    /// The body.
    pub body: Value,
    /// The calling app.
    pub app: String,
    /// The worker.
    pub worker: String,
    /// The first attempt's etos request id.
    pub etos_request_id: String,
    /// The first attempt's topic.
    pub topic: String,
}

/// What [`Ledger::insert_request`] did.
#[derive(Debug, Clone)]
pub enum Inserted {
    /// A new row, with its first event's cursor.
    Created(RequestView, i64),
    /// The same request was already there.
    Existing(RequestView),
}

/// A change to a request.
#[derive(Debug, Clone, Default)]
pub struct RequestUpdate {
    /// New state.
    pub state: Option<RequestState>,
    /// New etos status (`Some(None)` clears it).
    pub task_status: Option<Option<String>>,
    /// New attempt.
    pub attempt: Option<u32>,
    /// New outcome (`Some(None)` clears it).
    pub outcome: Option<Option<Value>>,
}

/// A stored candidate.
#[derive(Debug, Clone, PartialEq)]
pub struct CandidateRow {
    /// Change-set id.
    pub change_set_id: String,
    /// Request id.
    pub request_id: String,
    /// Producing task.
    pub task_id: String,
    /// Attempt.
    pub attempt: u32,
    /// The change set, verbatim.
    pub change_set: Value,
    /// `[StoredArtifact]`.
    pub artifacts: Value,
    /// `[Diagnostic]`.
    pub diagnostics: Value,
    /// Accepted (ms).
    pub received_at: i64,
}

/// A stored artifact's row.
#[derive(Debug, Clone, PartialEq)]
pub struct ArtifactRow {
    /// Digest.
    pub sha256: String,
    /// Path in the content store.
    pub path: String,
    /// Size.
    pub bytes: u64,
    /// Media type.
    pub media_type: String,
    /// Name.
    pub name: String,
    /// Role.
    pub role: Option<String>,
    /// Producer.
    pub producer: Value,
    /// Change set.
    pub change_set_id: Option<String>,
}

/// The ledger. Calls are short and synchronous behind one connection.
pub struct Ledger {
    conn: Mutex<Connection>,
}

impl std::fmt::Debug for Ledger {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Ledger").finish_non_exhaustive()
    }
}

fn state_of(s: &str) -> RequestState {
    RequestState::parse(s).unwrap_or(RequestState::Unresolved)
}

fn json_opt(s: Option<String>) -> LedgerResult<Option<Value>> {
    Ok(match s {
        Some(t) => Some(serde_json::from_str(&t)?),
        None => None,
    })
}

const REQUEST_COLS: &str = "request_id, change_set_id, digest, body, app, worker, state, task_status, attempt, outcome, seq, created_at, updated_at";

fn request_from(r: &rusqlite::Row<'_>) -> rusqlite::Result<(RequestRow, String, Option<String>)> {
    let body: String = r.get(3)?;
    let state: String = r.get(6)?;
    let outcome: Option<String> = r.get(9)?;
    Ok((
        RequestRow {
            request_id: r.get(0)?,
            change_set_id: r.get(1)?,
            digest: r.get(2)?,
            body: Value::Null,
            app: r.get(4)?,
            worker: r.get(5)?,
            state: state_of(&state),
            task_status: r.get(7)?,
            attempt: r.get::<_, i64>(8)?.try_into().unwrap_or(0),
            outcome: None,
            seq: r.get(10)?,
            created_at: r.get(11)?,
            updated_at: r.get(12)?,
        },
        body,
        outcome,
    ))
}

fn finish_request(t: (RequestRow, String, Option<String>)) -> LedgerResult<RequestRow> {
    let (mut row, body, outcome) = t;
    row.body = serde_json::from_str(&body)?;
    row.outcome = json_opt(outcome)?;
    Ok(row)
}

const ATTEMPT_COLS: &str =
    "request_id, attempt, etos_request_id, topic, task_id, parent_task, cursor, inputs, created_at";

fn attempt_from(r: &rusqlite::Row<'_>) -> rusqlite::Result<(AttemptRow, Option<String>)> {
    Ok((
        AttemptRow {
            request_id: r.get(0)?,
            attempt: r.get::<_, i64>(1)?.try_into().unwrap_or(0),
            etos_request_id: r.get(2)?,
            topic: r.get(3)?,
            task_id: r.get(4)?,
            parent_task: r.get(5)?,
            cursor: r.get::<_, i64>(6)?.try_into().unwrap_or(0),
            inputs: None,
            created_at: r.get(8)?,
        },
        r.get(7)?,
    ))
}

impl Ledger {
    /// Open (or create) the ledger at `path`.
    pub fn open(path: &Path) -> LedgerResult<Ledger> {
        let conn = Connection::open(path)?;
        conn.pragma_update(None, "journal_mode", "WAL")?;
        conn.pragma_update(None, "synchronous", "FULL")?;
        conn.busy_timeout(std::time::Duration::from_secs(5))?;
        conn.execute_batch(SCHEMA)?;
        // Ledgers written before these columns existed.
        for (col, ddl) in [
            ("resumes", "ALTER TABLE requests ADD COLUMN resumes INTEGER NOT NULL DEFAULT 0"),
            ("given_up", "ALTER TABLE requests ADD COLUMN given_up INTEGER NOT NULL DEFAULT 0"),
        ] {
            let present: i64 = conn.query_row(
                "SELECT COUNT(*) FROM pragma_table_info('requests') WHERE name = ?1",
                params![col],
                |r| r.get(0),
            )?;
            if present == 0 {
                conn.execute_batch(ddl)?;
            }
        }
        Ok(Ledger {
            conn: Mutex::new(conn),
        })
    }

    fn lock(&self) -> LedgerResult<MutexGuard<'_, Connection>> {
        self.conn.lock().map_err(|_| LedgerError::Poisoned)
    }

    // -----------------------------------------------------------------------------------------
    // Requests.

    /// Insert a request and its first attempt, or return the existing one when the same
    /// change-set id arrives again with the same body digest. A different body under the same
    /// id is a [`LedgerError::Conflict`].
    pub fn insert_request(&self, new: &NewRequest) -> LedgerResult<Inserted> {
        let mut conn = self.lock()?;
        let tx = conn.transaction()?;
        let existing: Option<String> = tx
            .query_row(
                "SELECT digest FROM requests WHERE request_id = ?1",
                params![new.change_set_id],
                |r| r.get(0),
            )
            .optional()?;
        if let Some(digest) = existing {
            if digest != new.digest {
                return Err(LedgerError::Conflict(format!(
                    "change set {} was already requested with a different body",
                    new.change_set_id
                )));
            }
            let view = view_in(&tx, &new.change_set_id)?;
            return Ok(Inserted::Existing(view));
        }
        let now = now_ms();
        tx.execute(
            &format!(
                "INSERT INTO requests ({REQUEST_COLS}) VALUES (?1, ?1, ?2, ?3, ?4, ?5, ?6, NULL, 0, NULL, 0, ?7, ?7)"
            ),
            params![
                new.change_set_id,
                new.digest,
                serde_json::to_string(&new.body)?,
                new.app,
                new.worker,
                RequestState::Requested.as_str(),
                now
            ],
        )?;
        tx.execute(
            &format!(
                "INSERT INTO attempts ({ATTEMPT_COLS}) VALUES (?1, 0, ?2, ?3, NULL, NULL, 0, NULL, ?4)"
            ),
            params![new.change_set_id, new.etos_request_id, new.topic, now],
        )?;
        let (view, cursor) = touch(&tx, &new.change_set_id)?;
        tx.commit()?;
        Ok(Inserted::Created(view, cursor))
    }

    /// A request.
    pub fn request(&self, request_id: &str) -> LedgerResult<RequestRow> {
        let conn = self.lock()?;
        request_in(&conn, request_id)
    }

    /// A request as the API shows it.
    pub fn request_view(&self, request_id: &str) -> LedgerResult<RequestView> {
        let conn = self.lock()?;
        view_in(&conn, request_id)
    }

    /// Requests of `app` changed after `after`, oldest change first.
    pub fn requests_after(
        &self,
        app: &str,
        after: i64,
        limit: usize,
    ) -> LedgerResult<Vec<RequestView>> {
        let conn = self.lock()?;
        let ids: Vec<String> = {
            let mut stmt = conn.prepare(
                "SELECT request_id FROM requests WHERE seq > ?1 AND app = ?3 ORDER BY seq ASC LIMIT ?2",
            )?;
            let rows = stmt.query_map(params![after, limit as i64, app], |r| r.get(0))?;
            rows.collect::<rusqlite::Result<_>>()?
        };
        ids.iter().map(|id| view_in(&conn, id)).collect()
    }

    /// Requests the companion still follows: non-terminal states, except `unresolved`
    /// requests it has given up on ([`Ledger::give_up`]).
    pub fn open_requests(&self) -> LedgerResult<Vec<RequestRow>> {
        let conn = self.lock()?;
        let mut stmt = conn.prepare(&format!(
            "SELECT {REQUEST_COLS} FROM requests WHERE state IN ('requested', 'running', 'waiting', 'unresolved') AND given_up = 0 ORDER BY created_at"
        ))?;
        let rows = stmt.query_map([], request_from)?;
        let raw: Vec<_> = rows.collect::<rusqlite::Result<_>>()?;
        raw.into_iter().map(finish_request).collect()
    }

    /// Count one more follow of a request after a restart; returns the new count.
    pub fn note_resume(&self, request_id: &str) -> LedgerResult<u32> {
        let conn = self.lock()?;
        conn.execute(
            "UPDATE requests SET resumes = resumes + 1 WHERE request_id = ?1",
            params![request_id],
        )?;
        let n: i64 = conn.query_row(
            "SELECT resumes FROM requests WHERE request_id = ?1",
            params![request_id],
            |r| r.get(0),
        )?;
        Ok(n.try_into().unwrap_or(u32::MAX))
    }

    /// Stop following an `unresolved` request for good, recording `outcome`; returns the new
    /// view and the event cursor. Other states are left unchanged (`None`).
    pub fn give_up(
        &self,
        request_id: &str,
        outcome: &Value,
    ) -> LedgerResult<Option<(RequestView, i64)>> {
        let mut conn = self.lock()?;
        let tx = conn.transaction()?;
        let current = request_in(&tx, request_id)?;
        if current.state != RequestState::Unresolved {
            return Ok(None);
        }
        tx.execute(
            "UPDATE requests SET given_up = 1, outcome = ?2, updated_at = ?3 WHERE request_id = ?1",
            params![request_id, serde_json::to_string(outcome)?, now_ms()],
        )?;
        let out = touch(&tx, request_id)?;
        tx.commit()?;
        Ok(Some(out))
    }

    /// Change a request; appends a `request` event. Returns the new view and the event cursor.
    ///
    /// Terminal states are final: a request already `candidate`, `candidate_invalid`,
    /// `needs_clarification`, `failed` or `cancelled` is left unchanged (no event; the cursor
    /// returned is its `seq`). A follower holding an older reading of the request (a cancel
    /// arrived meanwhile) therefore cannot overwrite the settled outcome.
    pub fn update_request(
        &self,
        request_id: &str,
        upd: &RequestUpdate,
    ) -> LedgerResult<(RequestView, i64)> {
        let mut conn = self.lock()?;
        let tx = conn.transaction()?;
        let out = match apply_in(&tx, request_id, upd)? {
            Some(out) => out,
            None => {
                let view = view_in(&tx, request_id)?;
                let seq = view.seq;
                return Ok((view, seq));
            }
        };
        tx.commit()?;
        Ok(out)
    }

    /// Start a re-ask in one transaction: insert attempt `a` and apply `upd` (the attempt
    /// pointer, state and outcome). `None` when the request is already terminal (nothing is
    /// written).
    pub fn begin_reask(
        &self,
        a: &AttemptRow,
        upd: &RequestUpdate,
    ) -> LedgerResult<Option<(RequestView, i64)>> {
        let mut conn = self.lock()?;
        let tx = conn.transaction()?;
        if request_in(&tx, &a.request_id)?.state.is_terminal() {
            return Ok(None);
        }
        insert_attempt_in(&tx, a)?;
        let out = apply_in(&tx, &a.request_id, upd)?;
        tx.commit()?;
        Ok(out)
    }

    /// Store a candidate and settle its request in one transaction. `None` when the request
    /// is already terminal (no candidate row is written).
    pub fn accept_candidate(
        &self,
        c: &CandidateRow,
        upd: &RequestUpdate,
    ) -> LedgerResult<Option<(RequestView, i64)>> {
        let mut conn = self.lock()?;
        let tx = conn.transaction()?;
        if request_in(&tx, &c.request_id)?.state.is_terminal() {
            return Ok(None);
        }
        put_candidate_in(&tx, c)?;
        let out = apply_in(&tx, &c.request_id, upd)?;
        tx.commit()?;
        Ok(out)
    }

    // -----------------------------------------------------------------------------------------
    // Attempts.

    /// One attempt.
    pub fn attempt(&self, request_id: &str, attempt: u32) -> LedgerResult<AttemptRow> {
        let conn = self.lock()?;
        attempt_in(&conn, request_id, attempt)
    }

    /// Every attempt of a request, oldest first.
    pub fn attempts(&self, request_id: &str) -> LedgerResult<Vec<AttemptRow>> {
        let conn = self.lock()?;
        attempts_in(&conn, request_id)
    }

    /// Add an attempt (a re-ask; see also [`Ledger::begin_reask`]).
    pub fn insert_attempt(&self, a: &AttemptRow) -> LedgerResult<()> {
        let conn = self.lock()?;
        insert_attempt_in(&conn, a)
    }

    /// Record the uploaded inputs of an attempt unless it has some already, and return the
    /// inputs the attempt now holds (the first writer wins: two openers never disagree on
    /// what the task was given).
    pub fn claim_attempt_inputs(
        &self,
        request_id: &str,
        attempt: u32,
        inputs: &Value,
    ) -> LedgerResult<Value> {
        let conn = self.lock()?;
        conn.execute(
            "UPDATE attempts SET inputs = ?3 WHERE request_id = ?1 AND attempt = ?2 AND inputs IS NULL",
            params![
                request_id,
                i64::from(attempt),
                serde_json::to_string(inputs)?
            ],
        )?;
        attempt_in(&conn, request_id, attempt)?
            .inputs
            .ok_or_else(|| LedgerError::NotFound(format!("inputs of attempt {attempt} of {request_id}")))
    }

    /// Record the task id of an attempt. A different task id for an attempt that already has
    /// one is a [`LedgerError::Conflict`] (etos returned another task for the same id).
    pub fn set_attempt_task(&self, request_id: &str, attempt: u32, task: &str) -> LedgerResult<()> {
        let conn = self.lock()?;
        let a = attempt_in(&conn, request_id, attempt)?;
        match a.task_id {
            Some(t) if t != task => Err(LedgerError::Conflict(format!(
                "attempt {attempt} of {request_id} is task {t}, etos now says {task}"
            ))),
            Some(_) => Ok(()),
            None => {
                conn.execute(
                    "UPDATE attempts SET task_id = ?3 WHERE request_id = ?1 AND attempt = ?2",
                    params![request_id, i64::from(attempt), task],
                )?;
                Ok(())
            }
        }
    }

    /// Record the last topic position processed for an attempt (never moves back).
    pub fn set_attempt_cursor(
        &self,
        request_id: &str,
        attempt: u32,
        cursor: u64,
    ) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "UPDATE attempts SET cursor = MAX(cursor, ?3) WHERE request_id = ?1 AND attempt = ?2",
            params![request_id, i64::from(attempt), cursor as i64],
        )?;
        Ok(())
    }

    // -----------------------------------------------------------------------------------------
    // Candidates and artifacts.

    /// Store a candidate (replacing an earlier one of the same change set); see also
    /// [`Ledger::accept_candidate`].
    pub fn put_candidate(&self, c: &CandidateRow) -> LedgerResult<()> {
        let conn = self.lock()?;
        put_candidate_in(&conn, c)
    }

    /// A candidate.
    pub fn candidate(&self, change_set_id: &str) -> LedgerResult<CandidateRow> {
        let conn = self.lock()?;
        let row = conn
            .query_row(
                "SELECT change_set_id, request_id, task_id, attempt, change_set, artifacts, diagnostics, received_at
                 FROM candidates WHERE change_set_id = ?1",
                params![change_set_id],
                |r| {
                    Ok((
                        r.get::<_, String>(0)?,
                        r.get::<_, String>(1)?,
                        r.get::<_, String>(2)?,
                        r.get::<_, i64>(3)?,
                        r.get::<_, String>(4)?,
                        r.get::<_, String>(5)?,
                        r.get::<_, String>(6)?,
                        r.get::<_, i64>(7)?,
                    ))
                },
            )
            .optional()?
            .ok_or_else(|| LedgerError::NotFound(format!("candidate {change_set_id}")))?;
        Ok(CandidateRow {
            change_set_id: row.0,
            request_id: row.1,
            task_id: row.2,
            attempt: row.3.try_into().unwrap_or(0),
            change_set: serde_json::from_str(&row.4)?,
            artifacts: serde_json::from_str(&row.5)?,
            diagnostics: serde_json::from_str(&row.6)?,
            received_at: row.7,
        })
    }

    /// Record an artifact (the first producer of a digest is kept).
    pub fn put_artifact(&self, a: &ArtifactRow) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "INSERT OR IGNORE INTO artifacts (sha256, path, bytes, media_type, name, role, producer, change_set_id, created_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
            params![
                a.sha256,
                a.path,
                a.bytes as i64,
                a.media_type,
                a.name,
                a.role,
                serde_json::to_string(&a.producer)?,
                a.change_set_id,
                now_ms()
            ],
        )?;
        Ok(())
    }

    /// An artifact's row.
    pub fn artifact(&self, sha256: &str) -> LedgerResult<ArtifactRow> {
        let conn = self.lock()?;
        let row = conn
            .query_row(
                "SELECT sha256, path, bytes, media_type, name, role, producer, change_set_id FROM artifacts WHERE sha256 = ?1",
                params![sha256],
                |r| {
                    Ok((
                        r.get::<_, String>(0)?,
                        r.get::<_, String>(1)?,
                        r.get::<_, i64>(2)?,
                        r.get::<_, String>(3)?,
                        r.get::<_, String>(4)?,
                        r.get::<_, Option<String>>(5)?,
                        r.get::<_, String>(6)?,
                        r.get::<_, Option<String>>(7)?,
                    ))
                },
            )
            .optional()?
            .ok_or_else(|| LedgerError::NotFound(format!("artifact {sha256}")))?;
        Ok(ArtifactRow {
            sha256: row.0,
            path: row.1,
            bytes: row.2.try_into().unwrap_or(0),
            media_type: row.3,
            name: row.4,
            role: row.5,
            producer: serde_json::from_str(&row.6)?,
            change_set_id: row.7,
        })
    }

    // -----------------------------------------------------------------------------------------
    // Events.

    /// Append an event; returns its cursor.
    pub fn append_event(
        &self,
        kind: &str,
        request_id: Option<&str>,
        data: &Value,
    ) -> LedgerResult<i64> {
        let conn = self.lock()?;
        conn.execute(
            "INSERT INTO events (at, kind, request_id, data) VALUES (?1, ?2, ?3, ?4)",
            params![now_ms(), kind, request_id, serde_json::to_string(data)?],
        )?;
        Ok(conn.last_insert_rowid())
    }

    /// Events after `after`, oldest first.
    pub fn events_after(&self, after: i64, limit: usize) -> LedgerResult<Vec<EventView>> {
        let conn = self.lock()?;
        let mut stmt = conn.prepare(
            "SELECT cursor, at, kind, request_id, data FROM events WHERE cursor > ?1 ORDER BY cursor ASC LIMIT ?2",
        )?;
        let rows = stmt.query_map(params![after, limit as i64], |r| {
            Ok((
                r.get::<_, i64>(0)?,
                r.get::<_, i64>(1)?,
                r.get::<_, String>(2)?,
                r.get::<_, Option<String>>(3)?,
                r.get::<_, String>(4)?,
            ))
        })?;
        let mut out = Vec::new();
        for row in rows {
            let (cursor, at, kind, request_id, data) = row?;
            out.push(EventView {
                cursor,
                at,
                kind,
                request_id,
                data: serde_json::from_str(&data)?,
            });
        }
        Ok(out)
    }

    /// The last event cursor (0 when empty).
    pub fn last_cursor(&self) -> LedgerResult<i64> {
        let conn = self.lock()?;
        Ok(
            conn.query_row("SELECT COALESCE(MAX(cursor), 0) FROM events", [], |r| {
                r.get(0)
            })?,
        )
    }

    // -----------------------------------------------------------------------------------------
    // Voice sessions.

    /// Record a voice session's start.
    pub fn voice_started(&self, session_id: &str, app: &str, provider: &str) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "INSERT OR IGNORE INTO voice_sessions (session_id, app, provider, started_at) VALUES (?1, ?2, ?3, ?4)",
            params![session_id, app, provider, now_ms()],
        )?;
        Ok(())
    }

    /// Record a voice session's end and counters.
    pub fn voice_ended(
        &self,
        session_id: &str,
        reason: &str,
        chunks: u64,
        audio_bytes: u64,
        transcripts: u64,
    ) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "UPDATE voice_sessions SET ended_at = ?2, reason = ?3, chunks = ?4, audio_bytes = ?5, transcripts = ?6 WHERE session_id = ?1",
            params![
                session_id,
                now_ms(),
                reason,
                chunks as i64,
                audio_bytes as i64,
                transcripts as i64
            ],
        )?;
        Ok(())
    }

    /// `(ended_at, reason, chunks, transcripts)` of a voice session.
    pub fn voice_session(
        &self,
        session_id: &str,
    ) -> LedgerResult<(Option<i64>, Option<String>, u64, u64)> {
        let conn = self.lock()?;
        conn.query_row(
            "SELECT ended_at, reason, chunks, transcripts FROM voice_sessions WHERE session_id = ?1",
            params![session_id],
            |r| {
                Ok((
                    r.get(0)?,
                    r.get(1)?,
                    r.get::<_, i64>(2)?.try_into().unwrap_or(0),
                    r.get::<_, i64>(3)?.try_into().unwrap_or(0),
                ))
            },
        )
        .optional()?
        .ok_or_else(|| LedgerError::NotFound(format!("voice session {session_id}")))
    }

    // -----------------------------------------------------------------------------------------
    // Stage jobs.

    /// Insert a stage job.
    pub fn insert_stage(&self, job: &StageJobView) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "INSERT INTO stage_jobs (job_id, change_set_id, package_ref, state, slot, verdict, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
            params![
                job.job_id,
                job.change_set_id,
                job.package_ref,
                job.state,
                job.slot,
                job.verdict.as_ref().map(Value::to_string),
                job.created_at,
                job.updated_at
            ],
        )?;
        Ok(())
    }

    /// Update a stage job's state, slot and verdict.
    pub fn update_stage(
        &self,
        job_id: &str,
        state: &str,
        slot: Option<&str>,
        verdict: Option<&Value>,
    ) -> LedgerResult<StageJobView> {
        {
            let conn = self.lock()?;
            conn.execute(
                "UPDATE stage_jobs SET state = ?2, slot = COALESCE(?3, slot), verdict = COALESCE(?4, verdict), updated_at = ?5 WHERE job_id = ?1",
                params![job_id, state, slot, verdict.map(Value::to_string), now_ms()],
            )?;
        }
        self.stage(job_id)
    }

    /// A stage job.
    pub fn stage(&self, job_id: &str) -> LedgerResult<StageJobView> {
        let conn = self.lock()?;
        let row = conn
            .query_row(
                "SELECT job_id, change_set_id, package_ref, state, slot, verdict, created_at, updated_at FROM stage_jobs WHERE job_id = ?1",
                params![job_id],
                |r| {
                    Ok((
                        r.get::<_, String>(0)?,
                        r.get::<_, String>(1)?,
                        r.get::<_, String>(2)?,
                        r.get::<_, String>(3)?,
                        r.get::<_, Option<String>>(4)?,
                        r.get::<_, Option<String>>(5)?,
                        r.get::<_, i64>(6)?,
                        r.get::<_, i64>(7)?,
                    ))
                },
            )
            .optional()?
            .ok_or_else(|| LedgerError::NotFound(format!("stage job {job_id}")))?;
        Ok(StageJobView {
            job_id: row.0,
            change_set_id: row.1,
            package_ref: row.2,
            state: row.3,
            slot: row.4,
            verdict: json_opt(row.5)?,
            created_at: row.6,
            updated_at: row.7,
        })
    }

    /// Stage jobs left `queued` or `running` by a previous process.
    pub fn unfinished_stages(&self) -> LedgerResult<Vec<String>> {
        let conn = self.lock()?;
        let mut stmt =
            conn.prepare("SELECT job_id FROM stage_jobs WHERE state IN ('queued', 'running')")?;
        let rows = stmt.query_map([], |r| r.get(0))?;
        Ok(rows.collect::<rusqlite::Result<_>>()?)
    }

    // -----------------------------------------------------------------------------------------
    // Tool catalogs and meta.

    /// Store a tool catalog under its revision. The same revision with other content is a
    /// [`LedgerError::Conflict`].
    pub fn put_catalog(&self, revision: &str, digest: &str, catalog: &Value) -> LedgerResult<()> {
        let conn = self.lock()?;
        let existing: Option<String> = conn
            .query_row(
                "SELECT digest FROM tool_catalogs WHERE revision = ?1",
                params![revision],
                |r| r.get(0),
            )
            .optional()?;
        match existing {
            Some(d) if d != digest => Err(LedgerError::Conflict(format!(
                "tool catalog revision {revision} was already stored with other content"
            ))),
            Some(_) => Ok(()),
            None => {
                conn.execute(
                    "INSERT INTO tool_catalogs (revision, digest, catalog, created_at) VALUES (?1, ?2, ?3, ?4)",
                    params![revision, digest, serde_json::to_string(catalog)?, now_ms()],
                )?;
                Ok(())
            }
        }
    }

    /// A tool catalog by revision.
    pub fn catalog(&self, revision: &str) -> LedgerResult<Option<Value>> {
        let conn = self.lock()?;
        let text: Option<String> = conn
            .query_row(
                "SELECT catalog FROM tool_catalogs WHERE revision = ?1",
                params![revision],
                |r| r.get(0),
            )
            .optional()?;
        json_opt(text)
    }

    /// Revisions of the stored tool catalogs, newest first.
    pub fn catalog_revisions(&self) -> LedgerResult<Vec<String>> {
        let conn = self.lock()?;
        let mut stmt =
            conn.prepare("SELECT revision FROM tool_catalogs ORDER BY created_at DESC LIMIT 20")?;
        let rows = stmt.query_map([], |r| r.get(0))?;
        Ok(rows.collect::<rusqlite::Result<_>>()?)
    }

    /// A meta value.
    pub fn meta(&self, key: &str) -> LedgerResult<Option<String>> {
        let conn = self.lock()?;
        Ok(conn
            .query_row("SELECT value FROM meta WHERE key = ?1", params![key], |r| {
                r.get(0)
            })
            .optional()?)
    }

    /// Set a meta value.
    pub fn set_meta(&self, key: &str, value: &str) -> LedgerResult<()> {
        let conn = self.lock()?;
        conn.execute(
            "INSERT INTO meta (key, value) VALUES (?1, ?2) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            params![key, value],
        )?;
        Ok(())
    }
}

/// Apply `upd` inside `tx` and append its `request` event. `None` (nothing written) when the
/// request is already terminal: terminal states are final.
fn apply_in(
    tx: &Transaction<'_>,
    request_id: &str,
    upd: &RequestUpdate,
) -> LedgerResult<Option<(RequestView, i64)>> {
    let current = request_in(tx, request_id)?;
    if current.state.is_terminal() {
        return Ok(None);
    }
    let now = now_ms();
    if let Some(s) = upd.state {
        tx.execute(
            "UPDATE requests SET state = ?2, updated_at = ?3 WHERE request_id = ?1",
            params![request_id, s.as_str(), now],
        )?;
    }
    if let Some(ts) = &upd.task_status {
        tx.execute(
            "UPDATE requests SET task_status = ?2, updated_at = ?3 WHERE request_id = ?1",
            params![request_id, ts, now],
        )?;
    }
    if let Some(a) = upd.attempt {
        tx.execute(
            "UPDATE requests SET attempt = ?2, updated_at = ?3 WHERE request_id = ?1",
            params![request_id, i64::from(a), now],
        )?;
    }
    if let Some(o) = &upd.outcome {
        let text = match o {
            Some(v) => Some(serde_json::to_string(&crate::util::pruned(v.clone()))?),
            None => None,
        };
        tx.execute(
            "UPDATE requests SET outcome = ?2, updated_at = ?3 WHERE request_id = ?1",
            params![request_id, text, now],
        )?;
    }
    Ok(Some(touch(tx, request_id)?))
}

fn insert_attempt_in(conn: &Connection, a: &AttemptRow) -> LedgerResult<()> {
    let inputs = match &a.inputs {
        Some(v) => Some(serde_json::to_string(v)?),
        None => None,
    };
    conn.execute(
        &format!(
            "INSERT OR IGNORE INTO attempts ({ATTEMPT_COLS}) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)"
        ),
        params![
            a.request_id,
            i64::from(a.attempt),
            a.etos_request_id,
            a.topic,
            a.task_id,
            a.parent_task,
            a.cursor as i64,
            inputs,
            a.created_at
        ],
    )?;
    Ok(())
}

fn put_candidate_in(conn: &Connection, c: &CandidateRow) -> LedgerResult<()> {
    conn.execute(
        "INSERT OR REPLACE INTO candidates (change_set_id, request_id, task_id, attempt, change_set, artifacts, diagnostics, received_at)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
        params![
            c.change_set_id,
            c.request_id,
            c.task_id,
            i64::from(c.attempt),
            serde_json::to_string(&c.change_set)?,
            serde_json::to_string(&c.artifacts)?,
            serde_json::to_string(&c.diagnostics)?,
            c.received_at
        ],
    )?;
    Ok(())
}

fn request_in(conn: &Connection, request_id: &str) -> LedgerResult<RequestRow> {
    let raw = conn
        .query_row(
            &format!("SELECT {REQUEST_COLS} FROM requests WHERE request_id = ?1"),
            params![request_id],
            request_from,
        )
        .optional()?
        .ok_or_else(|| LedgerError::NotFound(format!("request {request_id}")))?;
    finish_request(raw)
}

fn attempt_in(conn: &Connection, request_id: &str, attempt: u32) -> LedgerResult<AttemptRow> {
    let (mut row, inputs) = conn
        .query_row(
            &format!("SELECT {ATTEMPT_COLS} FROM attempts WHERE request_id = ?1 AND attempt = ?2"),
            params![request_id, i64::from(attempt)],
            attempt_from,
        )
        .optional()?
        .ok_or_else(|| LedgerError::NotFound(format!("attempt {attempt} of {request_id}")))?;
    row.inputs = json_opt(inputs)?;
    Ok(row)
}

fn attempts_in(conn: &Connection, request_id: &str) -> LedgerResult<Vec<AttemptRow>> {
    let mut stmt = conn.prepare(&format!(
        "SELECT {ATTEMPT_COLS} FROM attempts WHERE request_id = ?1 ORDER BY attempt"
    ))?;
    let rows = stmt.query_map(params![request_id], attempt_from)?;
    let mut out = Vec::new();
    for r in rows {
        let (mut row, inputs) = r?;
        row.inputs = json_opt(inputs)?;
        out.push(row);
    }
    Ok(out)
}

fn view_in(conn: &Connection, request_id: &str) -> LedgerResult<RequestView> {
    let r = request_in(conn, request_id)?;
    let attempts = attempts_in(conn, request_id)?;
    let current = attempts.iter().find(|a| a.attempt == r.attempt);
    let has_candidate: bool = conn
        .query_row(
            "SELECT COUNT(*) FROM candidates WHERE change_set_id = ?1",
            params![r.change_set_id],
            |row| row.get::<_, i64>(0),
        )
        .map(|n| n > 0)?;
    Ok(RequestView {
        request_id: r.request_id.clone(),
        change_set_id: r.change_set_id.clone(),
        worker: r.worker.clone(),
        state: r.state,
        task_id: current.and_then(|a| a.task_id.clone()),
        task_status: r.task_status.clone(),
        topic: current.map(|a| a.topic.clone()),
        attempt: r.attempt,
        tasks: attempts.iter().filter_map(|a| a.task_id.clone()).collect(),
        outcome: r.outcome.clone(),
        has_candidate,
        seq: r.seq,
        created_at: r.created_at,
        updated_at: r.updated_at,
    })
}

/// Append the `request` event for a change inside `tx` and store its cursor as `seq`.
fn touch(tx: &Transaction<'_>, request_id: &str) -> LedgerResult<(RequestView, i64)> {
    tx.execute(
        "INSERT INTO events (at, kind, request_id, data) VALUES (?1, 'request', ?2, '{}')",
        params![now_ms(), request_id],
    )?;
    let cursor = tx.last_insert_rowid();
    tx.execute(
        "UPDATE requests SET seq = ?2 WHERE request_id = ?1",
        params![request_id, cursor],
    )?;
    let view = view_in(tx, request_id)?;
    tx.execute(
        "UPDATE events SET data = ?2 WHERE cursor = ?1",
        params![cursor, serde_json::to_string(&view)?],
    )?;
    Ok((view, cursor))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn new(id: &str, digest: &str) -> NewRequest {
        NewRequest {
            change_set_id: id.into(),
            digest: digest.into(),
            body: json!({"changeSetId": id}),
            app: "gamecore-unity".into(),
            worker: "gc-designer".into(),
            etos_request_id: id.into(),
            topic: format!("#agent/gamecore-studio/cs-{id}"),
        }
    }

    #[test]
    fn requests_are_idempotent_and_conflicts_refused() {
        let dir = tempfile::tempdir().unwrap();
        let l = Ledger::open(&dir.path().join("l.db")).unwrap();
        let Inserted::Created(v, c) = l.insert_request(&new("cs1", "d1")).unwrap() else {
            panic!("not created")
        };
        assert_eq!(v.state, RequestState::Requested);
        assert_eq!(v.seq, c);
        assert!(matches!(
            l.insert_request(&new("cs1", "d1")).unwrap(),
            Inserted::Existing(_)
        ));
        assert!(matches!(
            l.insert_request(&new("cs1", "d2")),
            Err(LedgerError::Conflict(_))
        ));
        l.set_attempt_task("cs1", 0, "t1").unwrap();
        l.set_attempt_task("cs1", 0, "t1").unwrap();
        assert!(l.set_attempt_task("cs1", 0, "t2").is_err());
        l.set_attempt_cursor("cs1", 0, 5).unwrap();
        l.set_attempt_cursor("cs1", 0, 3).unwrap();
        assert_eq!(l.attempt("cs1", 0).unwrap().cursor, 5);
        let (v2, c2) = l
            .update_request(
                "cs1",
                &RequestUpdate {
                    state: Some(RequestState::Running),
                    task_status: Some(Some("queued".into())),
                    ..RequestUpdate::default()
                },
            )
            .unwrap();
        assert!(c2 > c);
        assert_eq!(v2.task_id.as_deref(), Some("t1"));
        assert_eq!(v2.task_status.as_deref(), Some("queued"));
        assert_eq!(l.requests_after("gamecore-unity", c, 10).unwrap().len(), 1);
        assert_eq!(l.requests_after("gamecore-unity", c2, 10).unwrap().len(), 0);
        assert_eq!(l.open_requests().unwrap().len(), 1);
        let ev = l.events_after(0, 10).unwrap();
        assert_eq!(ev.len(), 2);
        assert_eq!(ev[1].data["state"], "running");
        // Terminal states are final.
        let cancel = RequestUpdate {
            state: Some(RequestState::Cancelled),
            ..RequestUpdate::default()
        };
        let (v3, c3) = l.update_request("cs1", &cancel).unwrap();
        assert_eq!(v3.state, RequestState::Cancelled);
        let fail = RequestUpdate {
            state: Some(RequestState::Failed),
            task_status: Some(Some("failed".into())),
            ..RequestUpdate::default()
        };
        let (v4, c4) = l.update_request("cs1", &fail).unwrap();
        assert_eq!((v4.state, c4), (RequestState::Cancelled, c3));
        assert!(l.open_requests().unwrap().is_empty());
    }

    #[test]
    fn reask_candidate_inputs_and_give_up_are_atomic() {
        let dir = tempfile::tempdir().unwrap();
        let l = Ledger::open(&dir.path().join("l.db")).unwrap();
        l.insert_request(&new("cs1", "d1")).unwrap();
        // Inputs: the first writer wins.
        assert_eq!(l.claim_attempt_inputs("cs1", 0, &json!([1])).unwrap(), json!([1]));
        assert_eq!(l.claim_attempt_inputs("cs1", 0, &json!([2])).unwrap(), json!([1]));
        // A re-ask inserts the attempt and moves the pointer together.
        let a = AttemptRow {
            request_id: "cs1".into(),
            attempt: 1,
            etos_request_id: "cs1.r1".into(),
            topic: "t-r1".into(),
            task_id: None,
            parent_task: Some("t0".into()),
            cursor: 0,
            inputs: None,
            created_at: 1,
        };
        let upd = RequestUpdate {
            state: Some(RequestState::Running),
            attempt: Some(1),
            outcome: Some(Some(json!({"code": "candidate_invalid", "hint": null}))),
            ..RequestUpdate::default()
        };
        let (v, _) = l.begin_reask(&a, &upd).unwrap().unwrap();
        assert_eq!((v.attempt, v.topic.as_deref()), (1, Some("t-r1")));
        // Outcomes are stored without nulls.
        assert_eq!(v.outcome, Some(json!({"code": "candidate_invalid"})));
        let c = CandidateRow {
            change_set_id: "cs1".into(),
            request_id: "cs1".into(),
            task_id: "t1".into(),
            attempt: 1,
            change_set: json!({}),
            artifacts: json!([]),
            diagnostics: json!([]),
            received_at: 1,
        };
        let done = RequestUpdate {
            state: Some(RequestState::Candidate),
            ..RequestUpdate::default()
        };
        assert!(l.accept_candidate(&c, &done).unwrap().is_some());
        // Terminal: neither a second candidate nor a re-ask is written.
        assert!(l.accept_candidate(&c, &done).unwrap().is_none());
        let mut a2 = a.clone();
        a2.attempt = 2;
        assert!(l.begin_reask(&a2, &upd).unwrap().is_none());
        assert!(l.attempt("cs1", 2).is_err());
        // Giving up applies to unresolved requests only.
        assert!(l.give_up("cs1", &json!({"code": "unresolved"})).unwrap().is_none());
        l.insert_request(&new("cs2", "d")).unwrap();
        l.update_request(
            "cs2",
            &RequestUpdate {
                state: Some(RequestState::Unresolved),
                ..RequestUpdate::default()
            },
        )
        .unwrap();
        assert_eq!(l.note_resume("cs2").unwrap(), 1);
        assert_eq!(l.open_requests().unwrap().len(), 1);
        assert!(l.give_up("cs2", &json!({"code": "unresolved"})).unwrap().is_some());
        assert!(l.open_requests().unwrap().is_empty());
        // Scoped listing.
        assert!(l.requests_after("other-app", 0, 10).unwrap().is_empty());
    }

    #[test]
    fn catalogs_meta_and_stage_jobs() {
        let dir = tempfile::tempdir().unwrap();
        let l = Ledger::open(&dir.path().join("l.db")).unwrap();
        l.put_catalog("7", "a", &json!({"tools": []})).unwrap();
        l.put_catalog("7", "a", &json!({"tools": []})).unwrap();
        assert!(l.put_catalog("7", "b", &json!({})).is_err());
        assert_eq!(l.catalog("7").unwrap(), Some(json!({"tools": []})));
        assert_eq!(l.catalog("8").unwrap(), None);
        l.set_meta("port", "1").unwrap();
        l.set_meta("port", "2").unwrap();
        assert_eq!(l.meta("port").unwrap().as_deref(), Some("2"));
        let job = StageJobView {
            job_id: "j1".into(),
            change_set_id: "cs1".into(),
            package_ref: "ab".into(),
            state: "queued".into(),
            slot: None,
            verdict: None,
            created_at: 1,
            updated_at: 1,
        };
        l.insert_stage(&job).unwrap();
        assert_eq!(l.unfinished_stages().unwrap(), vec!["j1".to_string()]);
        let j = l
            .update_stage("j1", "done", Some("1"), Some(&json!({"ok": true})))
            .unwrap();
        assert_eq!(j.verdict, Some(json!({"ok": true})));
        assert_eq!(j.slot.as_deref(), Some("1"));
    }
}
