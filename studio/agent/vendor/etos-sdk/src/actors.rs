//! Core actor control for transaction agents. Requires the `actors` manifest grant.

use crate::util::seg;
use crate::{Client, Result};
use reqwest::Method;
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::time::Duration;

/// A pinned stored program (no dependency on core crates).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ActorProgramRef {
    /// Stored program descriptor.
    pub stored: StoredActorProgram,
}

/// Content-addressed actor code.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct StoredActorProgram {
    /// Content digest.
    pub digest: String,
    /// Module and class entry point.
    pub entry: String,
}

/// A registered program.
#[derive(Debug, Clone, Deserialize)]
pub struct ActorProgram {
    /// Pass this to spawn.
    pub program: ActorProgramRef,
    /// Python or JavaScript interpreter.
    pub kind: String,
    /// Worker scope of this registration.
    pub worker: String,
}

/// Spawn or adopt a root with a stable key. Use a new incarnation key after termination.
#[derive(Debug, Clone, Serialize)]
pub struct ActorSpawn {
    /// Registered program.
    pub program: ActorProgramRef,
    /// Exclusively owned worker.
    pub worker: String,
    /// Stable key; reusing it with different arguments is refused.
    pub key: String,
    /// Initialization arguments.
    pub args: Value,
    /// Exact agent-owned topics available to the actor and descendants.
    pub topics: Vec<String>,
}

/// Core actor metadata and committed state.
#[derive(Debug, Clone, Deserialize)]
pub struct ActorInfo {
    /// Runtime actor id.
    pub id: String,
    /// Running, idle, completed, failed or stopped.
    pub status: String,
    /// Last committed state.
    pub state: Option<Value>,
    /// Parent, for descendants.
    pub parent: Option<String>,
    /// Additional runtime metadata including scope, program, grants and counters.
    #[serde(flatten)]
    pub metadata: serde_json::Map<String, Value>,
}

/// Durable admission result. Accepted does not mean the message has executed.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ActorDelivery {
    /// Durably queued.
    Accepted,
    /// Already queued by a previous request.
    Duplicate,
    /// Recipient terminated before admission.
    Closed,
}

/// Message delivery acknowledgment.
#[derive(Debug, Clone, Deserialize)]
pub struct ActorSendAck {
    /// Application message id.
    pub id: String,
    /// Admission result.
    pub outcome: ActorDelivery,
}

/// Core actors owned by the calling agent.
#[derive(Debug, Clone)]
pub struct Actors {
    client: Client,
}

impl Client {
    /// Register programs and manage durable core actors.
    pub fn actors(&self) -> Actors {
        Actors {
            client: self.clone(),
        }
    }
}

impl Actors {
    /// Register a directory relative to this agent's installed files.
    pub async fn register(&self, path: &str, worker: &str) -> Result<ActorProgram> {
        self.client
            .json(
                Method::POST,
                "/agent/actors/programs",
                Some(&json!({"path": path, "worker": worker})),
                true,
                Duration::ZERO,
            )
            .await
    }
    /// Spawn or recover the actor for a stable key.
    pub async fn spawn(&self, request: &ActorSpawn) -> Result<ActorInfo> {
        self.client
            .json(
                Method::POST,
                "/agent/actors",
                Some(request),
                true,
                Duration::ZERO,
            )
            .await
    }
    /// Read an owned actor or descendant.
    pub async fn get(&self, id: &str) -> Result<ActorInfo> {
        self.client
            .json(
                Method::GET,
                &format!("/agent/actors/{}", seg(id)),
                None::<&Value>,
                true,
                Duration::ZERO,
            )
            .await
    }
    /// List owned roots and descendants.
    pub async fn list(&self) -> Result<Vec<ActorInfo>> {
        self.client
            .json(
                Method::GET,
                "/agent/actors",
                None::<&Value>,
                true,
                Duration::ZERO,
            )
            .await
    }
    /// Send an application message. Retry with the same id and payload after uncertainty.
    pub async fn send(
        &self,
        actor: &str,
        id: &str,
        kind: &str,
        body: Value,
    ) -> Result<ActorSendAck> {
        self.client
            .json(
                Method::POST,
                &format!("/agent/actors/{}/messages", seg(actor)),
                Some(&json!({"id": id, "kind": kind, "body": body})),
                true,
                Duration::ZERO,
            )
            .await
    }
    /// Stop an owned actor and descendants. Safe to repeat.
    pub async fn stop(&self, id: &str) -> Result<Vec<String>> {
        #[derive(Deserialize)]
        struct Ack {
            stopped: Vec<String>,
        }
        let ack: Ack = self
            .client
            .json(
                Method::POST,
                &format!("/agent/actors/{}/stop", seg(id)),
                Some(&json!({})),
                true,
                Duration::ZERO,
            )
            .await?;
        Ok(ack.stopped)
    }
}
