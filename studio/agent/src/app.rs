//! Lifecycle: open the ledger, bind the loopback listener, connect the agent channel, register
//! the `status` service and the proxied endpoint, resume open requests, and serve.
//!
//! On every (re)connect the endpoint is registered again (`PUT /agent/endpoint`; the SDK
//! repeats it after a reconnect and a monitor here repeats it once more when the connection
//! comes back). The proxy token is never cached: each request is checked against the token
//! of the current welcome, which etosd changes when it restarts.

use std::net::SocketAddr;
use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use etos_sdk::{Agent, AgentOptions, Client, Reply, ServiceSpec};
use serde_json::json;
use tokio::net::TcpListener;
use tokio::sync::watch;
use tokio::task::JoinHandle;

use crate::api::{AppState, Shared, router};
use crate::config::Config;
use crate::desk::Desk;
use crate::events::EventHub;
use crate::index::Indexer;
use crate::ledger::Ledger;
use crate::ops::MediaOps;
use crate::schema::ChangeSetSchema;
use crate::stage::StageRunner;
use crate::store::ArtifactStore;
use crate::voice::VoiceBridge;

/// Why the companion could not start.
#[derive(Debug, thiserror::Error)]
pub enum StartError {
    /// The state directory or listener failed.
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    /// The ledger failed.
    #[error("ledger: {0}")]
    Ledger(#[from] crate::ledger::LedgerError),
    /// The node refused or could not be reached.
    #[error("etos: {0}")]
    Etos(#[from] etos_sdk::Error),
    /// The change-set schema did not load.
    #[error("schema: {0}")]
    Schema(String),
}

/// A started companion.
pub struct Running {
    /// The loopback URL registered as the endpoint.
    pub url: String,
    /// Shared state (for tests and the binary).
    pub state: AppState,
    server: JoinHandle<()>,
    monitor: JoinHandle<()>,
    indexer_task: JoinHandle<()>,
    stop: watch::Sender<bool>,
}

impl std::fmt::Debug for Running {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Running")
            .field("url", &self.url)
            .finish_non_exhaustive()
    }
}

async fn bind(cfg: &Config, ledger: &Ledger) -> std::io::Result<TcpListener> {
    let saved = std::fs::read_to_string(cfg.state_dir.join("port"))
        .ok()
        .and_then(|s| s.trim().parse::<u16>().ok())
        .or_else(|| {
            ledger
                .meta("port")
                .ok()
                .flatten()
                .and_then(|s| s.parse().ok())
        });
    let wanted = cfg.port.or(saved);
    let listener = match wanted {
        Some(p) => match TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], p))).await {
            Ok(l) => l,
            Err(e) if cfg.port.is_none() => {
                tracing::warn!(port = p, error = %e, "saved port unavailable; using an ephemeral one");
                TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], 0))).await?
            }
            Err(e) => return Err(e),
        },
        None => TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], 0))).await?,
    };
    let port = listener.local_addr()?.port();
    std::fs::write(cfg.state_dir.join("port"), format!("{port}\n"))?;
    let _ = ledger.set_meta("port", &port.to_string());
    Ok(listener)
}

/// Start the companion with `client` (the agent's client; `ETOS_AGENT` names the agent).
pub async fn start(
    cfg: Config,
    client: Client,
    options: AgentOptions,
) -> Result<Running, StartError> {
    crate::blocking::spawn(start_inner(cfg, client, options))
        .await
        .map_err(|e| std::io::Error::other(e.to_string()))?
}

async fn start_inner(
    cfg: Config,
    client: Client,
    options: AgentOptions,
) -> Result<Running, StartError> {
    let client = if client.agent().is_some() {
        client
    } else {
        client.with_agent(&cfg.agent)
    };
    std::fs::create_dir_all(&cfg.state_dir)?;
    let state_dir = cfg.state_dir.clone();
    let ledger = Arc::new(Ledger::open(&state_dir.join("ledger.db"))?);
    let hub = EventHub::new(ledger.clone())?;
    let store = ArtifactStore::new(state_dir.join("artifacts"));
    let schema =
        Arc::new(ChangeSetSchema::load(cfg.schema.as_deref()).map_err(StartError::Schema)?);
    tracing::info!(schema = schema.source(), "change-set schema loaded");
    let listener = bind(&cfg, &ledger).await?;
    let addr = listener.local_addr()?;
    let url = format!("http://127.0.0.1:{}", addr.port());

    let agent = Agent::connect_with(client, options).await?;
    if let Some(w) = agent.welcome() {
        tracing::info!(agent = %w.agent, node = %w.node, sdk = %w.sdk, "welcomed by the node");
    }
    let client = agent.client().clone();
    let cfg = Arc::new(cfg);
    let indexer = Indexer::new(client.clone(), &cfg.agent, &state_dir, cfg.index_flush_ms);
    let indexer_task = indexer.start();
    let desk = Desk::new(
        cfg.clone(),
        client.clone(),
        ledger.clone(),
        hub.clone(),
        store.clone(),
        schema,
        indexer.clone(),
    )
    .map_err(StartError::Schema)?;
    let ops = Arc::new(
        MediaOps::new(
            client.clone(),
            ledger.clone(),
            store.clone(),
            hub.clone(),
            indexer.clone(),
            cfg.hello_cache_s,
            cfg.ops_max_cost_usd,
        )
        .with_prices(cfg.ops_prices.clone())
        .with_op_timeout(Duration::from_secs(cfg.ops_timeout_secs)),
    );
    let voice = Arc::new(VoiceBridge::new(
        client.clone(),
        cfg.voice.clone(),
        ledger.clone(),
        hub.clone(),
        ops.clone(),
    ));
    let stage = StageRunner::new(
        cfg.stage.clone(),
        ledger.clone(),
        hub.clone(),
        store.clone(),
        &state_dir,
    );
    stage.settle_interrupted();
    stage.probe_startup();
    let state: AppState = Arc::new(Shared {
        cfg: cfg.clone(),
        agent: agent.clone(),
        ledger: ledger.clone(),
        hub,
        store,
        desk: desk.clone(),
        ops,
        voice: voice.clone(),
        indexer: indexer.clone(),
        stage,
    });

    let (stop, stop_rx) = watch::channel(false);
    let app = router(state.clone());
    let mut server_stop = stop_rx.clone();
    let server = tokio::spawn(async move {
        let shutdown = async move {
            let _ = server_stop.wait_for(|s| *s).await;
        };
        if let Err(e) = axum::serve(listener, app)
            .with_graceful_shutdown(shutdown)
            .await
        {
            tracing::error!(error = %e, "the loopback listener failed");
        }
    });

    let status_state = state.clone();
    agent
        .service(
            "status",
            ServiceSpec::new().deadline_ms(5_000),
            move |_call| {
                let s = status_state.clone();
                async move {
                    Ok(Reply::data(json!({
                        "service": s.cfg.agent,
                        "version": crate::VERSION,
                        "connected": s.agent.is_connected(),
                        "following": s.desk.following(),
                        "voiceSessions": s.voice.active(),
                        "indexPending": s.indexer.pending(),
                        "indexRevision": s.indexer.revision(),
                    })))
                }
            },
        )
        .await?;
    agent.endpoint(&url).await?;
    tracing::info!(url = %url, "endpoint registered");
    let resumed = desk.resume_all()?;
    if resumed > 0 {
        tracing::info!(requests = resumed, "resumed open requests");
    }
    let monitor = tokio::spawn(monitor(agent.clone(), url.clone(), stop_rx));
    Ok(Running {
        url,
        state,
        server,
        monitor,
        indexer_task,
        stop,
    })
}

/// Re-register the endpoint whenever the channel comes back or the welcome's proxy token
/// changes (idempotent at the node); a failed registration is retried on the next tick.
async fn monitor(agent: Agent, url: String, mut stop: watch::Receiver<bool>) {
    let mut was = agent.is_connected();
    let mut token = agent.welcome().map(|w| w.proxy_token());
    let mut pending = false;
    loop {
        tokio::select! {
            _ = tokio::time::sleep(Duration::from_millis(500)) => {}
            _ = stop.wait_for(|s| *s) => return,
        }
        let now = agent.is_connected();
        let current = agent.welcome().map(|w| w.proxy_token());
        if current != token {
            tracing::info!("the node issued a new proxy token (reconnect or restart)");
            token = current;
            pending = true;
        }
        if now && !was {
            pending = true;
        }
        if now && pending {
            match agent.endpoint(&url).await {
                Ok(_) => {
                    pending = false;
                    tracing::info!(url = %url, "endpoint registered again");
                }
                Err(e) => tracing::warn!(error = %e, "endpoint registration failed; retrying"),
            }
        }
        was = now;
    }
}

impl Running {
    /// Report ready and serve until the node's `shutdown` (or a refusal).
    pub async fn serve(&self) -> etos_sdk::Result<()> {
        self.state.agent.serve().await
    }

    /// Stop: followers, index delivery (flushed), listener, channel.
    pub async fn shutdown(self) {
        self.state.desk.shutdown().await;
        self.state.indexer.close().await;
        let _ = self.stop.send(true);
        let _ = tokio::time::timeout(Duration::from_secs(5), self.server).await;
        self.monitor.abort();
        self.indexer_task.abort();
        self.state.agent.close();
        tracing::info!("companion stopped");
    }

    /// The state directory.
    pub fn state_dir(&self) -> &Path {
        &self.state.cfg.state_dir
    }
}
