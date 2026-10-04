//! The logger: declared states, validated changes, numbered traces, batching, and
//! exactly-once delivery (the SDK numbers each trace with its producer id and sequence, saves
//! the next number before sending, and the node deduplicates on (producer, seq)).

use std::collections::BTreeMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use reqwest::Method;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use tokio::sync::Notify;

use crate::client::Client;
use crate::error::{Error, Result};
use crate::util::{canonical_json, new_id, seg, sha256};
use crate::wire::{
    BindingAck, BindingDeclaration, PropType, StateSpec, Trace, TraceAck, TraceBatch,
};

/// What a logger persists so a restarted process continues its numbering.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ProducerState {
    /// The producer id.
    pub producer: String,
    /// The next sequence number to assign.
    pub next: u64,
}

/// Durable storage for [`ProducerState`]. Without one, each process is a new producer.
pub trait ProducerStore: Send + Sync {
    /// The saved state, if any.
    fn load(&self) -> Result<Option<ProducerState>>;
    /// Save the state; it must be durable when this returns.
    fn save(&self, state: &ProducerState) -> Result<()>;
}

/// A [`ProducerStore`] in a JSON file (for an agent: under `ETOS_STATE_DIR`). Written to a
/// temporary file and renamed, so a crash leaves the old or the new state, never a torn one.
#[derive(Debug, Clone)]
pub struct FileStore {
    path: PathBuf,
}

impl FileStore {
    /// A store at `path`.
    pub fn new(path: impl Into<PathBuf>) -> FileStore {
        FileStore { path: path.into() }
    }
}

impl ProducerStore for FileStore {
    fn load(&self) -> Result<Option<ProducerState>> {
        match std::fs::read(&self.path) {
            Ok(bytes) => serde_json::from_slice(&bytes).map(Some).map_err(|e| {
                Error::Invalid(format!(
                    "the producer state in {} is malformed: {e}",
                    self.path.display()
                ))
            }),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(e) => Err(Error::Invalid(format!(
                "cannot read {}: {e}",
                self.path.display()
            ))),
        }
    }

    fn save(&self, state: &ProducerState) -> Result<()> {
        let io = |e: std::io::Error| {
            Error::Invalid(format!("cannot write {}: {e}", self.path.display()))
        };
        let tmp = self.path.with_extension("tmp");
        let bytes = serde_json::to_vec(state)
            .map_err(|e| Error::Invalid(format!("cannot encode the producer state: {e}")))?;
        {
            use std::io::Write;
            let mut f = std::fs::File::create(&tmp).map_err(io)?;
            f.write_all(&bytes).map_err(io)?;
            f.sync_all().map_err(io)?;
        }
        std::fs::rename(&tmp, &self.path).map_err(io)
    }
}

/// Logger configuration.
pub struct LoggerOptions {
    app: String,
    user: Option<String>,
    states: BTreeMap<String, StateSpec>,
    store: Option<Arc<dyn ProducerStore>>,
    batch_size: usize,
    flush_interval: Duration,
    max_buffered: usize,
}

impl LoggerOptions {
    /// Log the states of `app` (for an agent: its own name, its own binding).
    pub fn new(app: &str, states: BTreeMap<String, StateSpec>) -> LoggerOptions {
        LoggerOptions {
            app: app.to_string(),
            user: None,
            states,
            store: None,
            batch_size: 500,
            flush_interval: Duration::from_secs(1),
            max_buffered: 100_000,
        }
    }

    /// The default user: the partition of every non-global state.
    pub fn user(mut self, user: &str) -> LoggerOptions {
        self.user = Some(user.to_string());
        self
    }

    /// Persist the producer id and sequence.
    pub fn store(mut self, store: impl ProducerStore + 'static) -> LoggerOptions {
        self.store = Some(Arc::new(store));
        self
    }

    /// Traces per request (default 500).
    pub fn batch_size(mut self, n: usize) -> LoggerOptions {
        self.batch_size = n.max(1);
        self
    }

    /// Longest time a trace waits in the buffer (default 1 s).
    pub fn flush_interval(mut self, d: Duration) -> LoggerOptions {
        self.flush_interval = d;
        self
    }

    /// Buffered traces beyond which `change` refuses with `backpressure` (default 100 000).
    pub fn max_buffered(mut self, n: usize) -> LoggerOptions {
        self.max_buffered = n.max(1);
        self
    }
}

/// Optional metadata of a change.
#[derive(Debug, Clone, Default)]
pub struct ChangeMeta {
    /// Operation name.
    pub op: Option<String>,
    /// Event time in the app (ms since the epoch).
    pub at: Option<i64>,
    /// Who caused the change.
    pub by: Option<String>,
    /// Groups the changes of one request.
    pub correlation: Option<String>,
}

/// A logger. Cheap to clone; [`Logger::with_user`] views share its buffer and numbering.
#[derive(Clone)]
pub struct Logger {
    core: Arc<Core>,
    user: Option<String>,
}

struct Core {
    client: Client,
    app: String,
    states: BTreeMap<String, StateSpec>,
    store: Option<Arc<dyn ProducerStore>>,
    batch_size: usize,
    max_buffered: usize,
    buf: Mutex<Buffers>,
    /// Serializes delivery rounds.
    round: tokio::sync::Mutex<()>,
    wake: Notify,
}

struct Buffers {
    producer: ProducerState,
    /// Validated, not yet numbered.
    incoming: Vec<Trace>,
    /// Numbered (the next number is saved), not yet acknowledged.
    outgoing: Vec<Trace>,
    closed: bool,
}

impl Client {
    /// Start a logger: load (or create) the producer state and declare the binding. A
    /// background task delivers buffered traces every `flush_interval`.
    pub async fn logger(&self, options: LoggerOptions) -> Result<Logger> {
        check_states(&options.states)?;
        let producer = match &options.store {
            Some(store) => store.load()?,
            None => None,
        };
        let producer = match producer {
            Some(p) if p.next >= 1 => p,
            Some(_) => {
                return Err(Error::Invalid(
                    "the producer store returned a sequence number below 1".into(),
                ));
            }
            None => {
                let p = ProducerState {
                    producer: new_id("prod"),
                    next: 1,
                };
                if let Some(store) = &options.store {
                    store.save(&p)?;
                }
                p
            }
        };
        let states_value = serde_json::to_value(&options.states)
            .map_err(|e| Error::Invalid(format!("cannot encode the states: {e}")))?;
        let decl = BindingDeclaration {
            digest: sha256(&canonical_json(&states_value)),
            states: options.states.clone(),
        };
        let _: BindingAck = self
            .json(
                Method::PUT,
                &format!("/bindings/{}", seg(&options.app)),
                Some(&decl),
                true,
                Duration::ZERO,
            )
            .await?;
        let core = Arc::new(Core {
            client: self.clone(),
            app: options.app,
            states: options.states,
            store: options.store,
            batch_size: options.batch_size,
            max_buffered: options.max_buffered,
            buf: Mutex::new(Buffers {
                producer,
                incoming: Vec::new(),
                outgoing: Vec::new(),
                closed: false,
            }),
            round: tokio::sync::Mutex::new(()),
            wake: Notify::new(),
        });
        tokio::spawn(background(Arc::downgrade(&core), options.flush_interval));
        Ok(Logger {
            core,
            user: options.user,
        })
    }
}

/// Deliver buffered traces every `interval`, or at once when a batch is full; back off while
/// the node is unreachable. Ends when the logger is dropped or closed.
async fn background(core: Weak<Core>, interval: Duration) {
    let mut failures: u32 = 0;
    loop {
        let wait = if failures == 0 {
            interval
        } else {
            (interval * 2u32.saturating_pow(failures.min(6))).min(Duration::from_secs(60))
        };
        {
            // Holding the core while waiting only delays freeing a dropped logger by one wait.
            let Some(c) = core.upgrade() else { return };
            let _ = tokio::time::timeout(wait, c.wake.notified()).await;
        }
        let Some(c) = core.upgrade() else { return };
        if c.closed() {
            return;
        }
        match c.round().await {
            Ok(()) => failures = 0,
            Err(e) => {
                failures = failures.saturating_add(1);
                if !e.is_retryable() {
                    tracing::warn!(app = %c.app, error = %e, "etos logger: traces were refused and dropped");
                }
            }
        }
    }
}

impl Core {
    fn closed(&self) -> bool {
        self.buf.lock().map(|b| b.closed).unwrap_or(true)
    }

    fn buffers(&self) -> Result<std::sync::MutexGuard<'_, Buffers>> {
        self.buf
            .lock()
            .map_err(|_| Error::Invalid("the logger failed earlier and is unusable".into()))
    }

    /// One delivery round: number the incoming traces, save the next number, then send the
    /// outgoing ones in batches. A batch the node refuses permanently is dropped and its
    /// error returned after the rest are sent; a retryable failure stops the round.
    async fn round(&self) -> Result<()> {
        let _serial = self.round.lock().await;
        let snapshot = {
            let mut b = self.buffers()?;
            if b.incoming.is_empty() {
                None
            } else {
                let mut incoming = std::mem::take(&mut b.incoming);
                for t in &mut incoming {
                    t.producer = b.producer.producer.clone();
                    t.seq = b.producer.next;
                    b.producer.next += 1;
                }
                b.outgoing.extend(incoming);
                Some(b.producer.clone())
            }
        };
        if let (Some(state), Some(store)) = (snapshot, &self.store) {
            // Before sending: a restart never reuses a number the node has seen.
            store.save(&state)?;
        }
        let path = format!("/bindings/{}/traces", seg(&self.app));
        let mut refused: Option<Error> = None;
        loop {
            let batch: Vec<Trace> = {
                let b = self.buffers()?;
                b.outgoing.iter().take(self.batch_size).cloned().collect()
            };
            if batch.is_empty() {
                break;
            }
            let n = batch.len();
            let body = TraceBatch { traces: batch };
            let sent: Result<TraceAck> = self
                .client
                .json(Method::POST, &path, Some(&body), true, Duration::ZERO)
                .await;
            match sent {
                Ok(_) => {}
                Err(e) if e.is_retryable() => return Err(e),
                Err(e) => refused = refused.or(Some(e)),
            }
            {
                let mut b = self.buffers()?;
                let rest = b.outgoing.split_off(n);
                b.outgoing = rest;
            }
        }
        refused.map_or(Ok(()), Err)
    }
}

impl Logger {
    /// A view of this logger for another user; it shares the buffer and the numbering.
    pub fn with_user(&self, user: &str) -> Logger {
        Logger {
            core: self.core.clone(),
            user: Some(user.to_string()),
        }
    }

    /// Log a change of one entity: `values` holds the changed properties and links only
    /// (`null` clears one). Checked here against the declared states, then buffered.
    pub fn change(&self, kind: &str, key: &str, values: Value, meta: ChangeMeta) -> Result<()> {
        let spec = self.core.states.get(kind).ok_or_else(|| {
            Error::Invalid(format!(
                "state {kind:?} is not declared; declared: {}",
                self.core
                    .states
                    .keys()
                    .cloned()
                    .collect::<Vec<_>>()
                    .join(", ")
            ))
        })?;
        if key.is_empty() || key.len() > 512 {
            return Err(Error::Invalid(format!(
                "the key of {kind:?} must be 1 to 512 bytes"
            )));
        }
        let values = check_values(kind, spec, key, values)?;
        let user = if spec.global {
            None
        } else {
            Some(self.user.clone().ok_or_else(|| {
                Error::Invalid(format!(
                    "state {kind:?} is per user, but this logger names no user; use with_user"
                ))
            })?)
        };
        let full = {
            let mut b = self.core.buffers()?;
            if b.closed {
                return Err(Error::Invalid("the logger is closed".into()));
            }
            if b.incoming.len() + b.outgoing.len() >= self.core.max_buffered {
                return Err(Error::Invalid(format!(
                    "backpressure: {} traces are waiting for delivery; the node is unreachable or slow",
                    b.incoming.len() + b.outgoing.len()
                )));
            }
            b.incoming.push(Trace {
                producer: String::new(),
                seq: 0,
                user,
                kind: kind.to_string(),
                key: key.to_string(),
                values,
                op: meta.op,
                at: meta.at,
                by: meta.by,
                correlation: meta.correlation,
            });
            b.incoming.len() >= self.core.batch_size
        };
        if full {
            self.core.wake.notify_one();
        }
        Ok(())
    }

    /// Deliver everything buffered now. A batch the node refused is dropped and its refusal
    /// returned; an unreachable node is retried within the client's retry budget.
    pub async fn flush(&self) -> Result<()> {
        self.core.round().await
    }

    /// Flush, then stop the background delivery. Call it on shutdown: traces still buffered
    /// when the process dies are lost.
    pub async fn close(&self) -> Result<()> {
        let flushed = self.core.round().await;
        self.core.buffers()?.closed = true;
        self.core.wake.notify_one();
        flushed
    }

    /// The producer id.
    pub fn producer(&self) -> Result<String> {
        Ok(self.core.buffers()?.producer.producer.clone())
    }
}

fn check_states(states: &BTreeMap<String, StateSpec>) -> Result<()> {
    if states.is_empty() {
        return Err(Error::Invalid("declare at least one state".into()));
    }
    for (kind, s) in states {
        for (name, target) in &s.links {
            if s.props.contains_key(name) {
                return Err(Error::Invalid(format!(
                    "state {kind:?}: {name:?} is both a property and a link"
                )));
            }
            if !states.contains_key(target) {
                return Err(Error::Invalid(format!(
                    "state {kind:?}: link {name:?} points to {target:?}, which is not declared"
                )));
            }
        }
        for (name, p) in &s.props {
            if p.values.is_some() && p.ty != PropType::Enum {
                return Err(Error::Invalid(format!(
                    "state {kind:?}: property {name:?} lists values but is not an enum"
                )));
            }
        }
        if s.private && s.global {
            return Err(Error::Invalid(format!(
                "state {kind:?} cannot be both private and global"
            )));
        }
    }
    Ok(())
}

fn check_values(
    kind: &str,
    spec: &StateSpec,
    key: &str,
    values: Value,
) -> Result<Map<String, Value>> {
    let Value::Object(values) = values else {
        return Err(Error::Invalid(format!(
            "the values of {kind:?} must be a JSON object"
        )));
    };
    let mut out = Map::new();
    for (name, v) in values {
        let bad =
            |expected: &str| Error::Invalid(format!("{kind}.{name} must be {expected}, not {v}"));
        if let Some(p) = spec.props.get(&name) {
            let ok = v.is_null()
                || match p.ty {
                    PropType::String | PropType::Text => v.is_string(),
                    PropType::Enum => v.as_str().is_some_and(|s| {
                        p.values.as_ref().is_none_or(|vs| vs.iter().any(|x| x == s))
                    }),
                    PropType::Int => v.is_i64() || v.is_u64(),
                    PropType::Number => v.is_number(),
                    PropType::Money => v.is_number() || v.as_str().is_some_and(is_decimal),
                    PropType::Bool => v.is_boolean(),
                    PropType::Time => v.is_i64(),
                    PropType::Json => true,
                };
            if !ok {
                let expected = match p.ty {
                    PropType::String | PropType::Text => "a string",
                    PropType::Enum => "one of the enum's values",
                    PropType::Int => "an integer",
                    PropType::Number => "a number",
                    PropType::Money => "a number or a decimal string",
                    PropType::Bool => "a boolean",
                    PropType::Time => "integer milliseconds since the epoch",
                    PropType::Json => "JSON",
                };
                return Err(bad(expected));
            }
            out.insert(name, v);
        } else if spec.links.contains_key(&name) {
            let key_ok = |k: &Value| k.is_string() || k.is_i64() || k.is_u64();
            let ok =
                v.is_null() || key_ok(&v) || v.as_array().is_some_and(|a| a.iter().all(key_ok));
            if !ok {
                return Err(bad("a key or a list of keys"));
            }
            out.insert(name, v);
        } else if name == spec.key {
            // A string key, or a number written as the key.
            let written = v.to_string();
            if v.as_str() != Some(key) && written != key {
                return Err(Error::Invalid(format!(
                    "{kind}.{name} differs from the key given to change()"
                )));
            }
        } else {
            return Err(Error::Invalid(format!(
                "{kind:?} has no property or link {name:?}"
            )));
        }
    }
    Ok(out)
}

fn is_decimal(s: &str) -> bool {
    let s = s.strip_prefix('-').unwrap_or(s);
    let (int, frac) = s.split_once('.').unwrap_or((s, "0"));
    !int.is_empty()
        && !frac.is_empty()
        && int.bytes().all(|b| b.is_ascii_digit())
        && frac.bytes().all(|b| b.is_ascii_digit())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::wire::PropSpec;
    use serde_json::json;

    fn order() -> StateSpec {
        StateSpec::new("id")
            .prop(
                "status",
                PropSpec {
                    ty: PropType::Enum,
                    values: Some(vec!["new".into(), "paid".into()]),
                    description: None,
                },
            )
            .prop("total", PropType::Money)
            .link("customer", "customer")
    }

    #[test]
    fn values_are_checked_against_the_state() {
        let s = order();
        assert!(
            check_values(
                "order",
                &s,
                "o1",
                json!({"status": "paid", "total": "12.50", "customer": "c1"})
            )
            .is_ok()
        );
        assert!(check_values("order", &s, "o1", json!({"status": "shipped"})).is_err());
        assert!(check_values("order", &s, "o1", json!({"total": "12,50"})).is_err());
        assert!(check_values("order", &s, "o1", json!({"colour": "red"})).is_err());
        assert!(check_values("order", &s, "o1", json!({"id": "o2"})).is_err());
        assert!(check_values("order", &s, "o1", json!({"customer": [1, "c2"]})).is_ok());
        assert!(is_decimal("-3.25") && is_decimal("7") && !is_decimal("1.") && !is_decimal("x"));
    }

    #[test]
    fn states_are_checked() {
        let mut states = BTreeMap::new();
        states.insert("order".to_string(), order());
        assert!(check_states(&states).is_err(), "customer is not declared");
        states.insert("customer".to_string(), StateSpec::new("id").referenced());
        assert!(check_states(&states).is_ok());
    }
}
