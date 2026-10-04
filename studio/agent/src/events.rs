//! The event hub: events are appended to the ledger (durable, ordered by cursor) and the
//! latest cursor is published on a watch channel so `WS /v1/events` subscribers wake up and
//! read what they have not sent yet. Readers never miss an event: they read the ledger from
//! their own cursor, the watch only says "there is more".

use std::sync::Arc;

use serde_json::Value;
use tokio::sync::watch;

use crate::ledger::{Ledger, LedgerResult};

/// Durable, ordered events with wake-ups.
#[derive(Debug, Clone)]
pub struct EventHub {
    ledger: Arc<Ledger>,
    latest: Arc<watch::Sender<i64>>,
}

impl EventHub {
    /// A hub over the ledger, starting at its last cursor.
    pub fn new(ledger: Arc<Ledger>) -> LedgerResult<EventHub> {
        let last = ledger.last_cursor()?;
        let (tx, _) = watch::channel(last);
        Ok(EventHub {
            ledger,
            latest: Arc::new(tx),
        })
    }

    /// Append an event and wake subscribers; returns its cursor.
    pub fn emit(&self, kind: &str, request_id: Option<&str>, data: &Value) -> LedgerResult<i64> {
        let cursor = self.ledger.append_event(kind, request_id, data)?;
        self.notify(cursor);
        Ok(cursor)
    }

    /// Wake subscribers after the ledger appended an event itself (request updates).
    pub fn notify(&self, cursor: i64) {
        self.latest.send_if_modified(|c| {
            if cursor > *c {
                *c = cursor;
                true
            } else {
                false
            }
        });
    }

    /// A receiver of the latest cursor.
    pub fn subscribe(&self) -> watch::Receiver<i64> {
        self.latest.subscribe()
    }

    /// The ledger.
    pub fn ledger(&self) -> &Arc<Ledger> {
        &self.ledger
    }
}
