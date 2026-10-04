//! `etos-sdk`: agents and apps on etos, in Rust (design-agents §5).
//!
//! One [`Client`] talks to one node's SDK API (`/api/v1`) with an app key or an agent key.
//!
//! - **Agents** ([`Agent`]): the channel the node pushes tool and service calls over, with
//!   reconnection and redelivery handled here; [`Agent::tool`], [`Agent::service`],
//!   [`Agent::endpoint`] for proxied connections; and through the client the operations
//!   ([`Client::ops`]), provider passthrough ([`providers::url`]), the change feed
//!   ([`Client::changes`]), tasks, topics and files.
//! - **Apps**: the logger ([`Client::logger`]), app sources ([`Client::register_source`]),
//!   entrances ([`Client::entrance`]), queries with bind parameters ([`Client::query`]), and
//!   the services of the agents an app uses ([`Client::call_service`]).
//!
//! The SDK depends on no core crate: its wire types are its own, checked against the node's
//! JSON Schema by a test.

mod actors;
mod agent;
mod client;
mod data;
mod entrance;
mod error;
mod logger;
pub mod realtime;
mod source;
mod util;
pub mod wire;

pub use actors::{
    ActorDelivery, ActorInfo, ActorProgram, ActorProgramRef, ActorSendAck, ActorSpawn, Actors,
    StoredActorProgram,
};
pub use agent::{
    Agent, AgentOptions, Call, CallContext, CallKind, ProxyToken, Reply, SDK, ServiceSpec,
    ToolSpec, Welcome,
};
pub use client::{Client, Query};
pub use data::{Changes, Files, Ops, Tasks, Topics, providers};
pub use entrance::{Ask, Attachment, Conversation, Entrance};
pub use error::{Error, Refusal, Result};
pub use logger::{ChangeMeta, FileStore, Logger, LoggerOptions, ProducerState, ProducerStore};
pub use source::{SourceAnswer, SourceHandle};
pub use wire::{
    ChangeEntry, FileInfo, ParamValue, PlannedTable, QueryPlan, QueryResponse, RecordStatus,
    ResultColumn, Retry, TaskInfo, TaskRequest, TopicAck, TopicPage, TopicPost, TopicRecord,
};
