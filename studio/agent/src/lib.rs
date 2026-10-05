//! `gamecore-studio`: the GameCore Studio companion, an installed etos agent
//! (docs/studio/02-architecture.md boundary D; docs/studio/04-etos-integration.md is its
//! contract).
//!
//! The process holds the only etos *agent* key of the product. Unity reaches it through the
//! node's proxied agent route (`/api/v1/agents/gamecore-studio/http/...`) with its app key;
//! every proxied request carries `X-Etos-Proxy-Token` (checked against the current welcome)
//! and `X-Etos-App` (must be the configured Unity app). The companion:
//!
//! - keeps a durable SQLite ledger of requests, task attempts, candidates, artifacts, events,
//!   voice sessions and stage jobs ([`ledger`]);
//! - packs context, opens etos tasks on the Studio workers, follows their topics, verifies and
//!   validates the change sets they return, and stores artifacts content-addressed ([`desk`],
//!   [`candidate`], [`store`]);
//! - runs media operations ([`ops`]), bridges realtime voice for transcription only
//!   ([`voice`]), publishes the semantic index to the Resource Graph ([`index`]) and runs the
//!   staging shell ([`stage`]);
//! - serves the Unity-facing HTTP/WebSocket API ([`api`]).
//!
//! [`app::start`] wires it all together; `src/main.rs` is the supervised binary.

pub mod api;
pub mod app;
pub mod blocking;
pub mod candidate;
pub mod config;
pub mod desk;
pub mod error;
pub mod events;
pub mod index;
pub mod ledger;
pub mod model;
pub mod ops;
pub mod redact;
pub mod schema;
pub mod stage;
pub mod store;
pub mod util;
pub mod voice;

/// The companion's version (from Cargo).
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

/// The Unity-facing protocol version reported by `/v1/hello`.
pub const PROTOCOL: u32 = 1;
