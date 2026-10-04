//! `gamecore-studio`: the process etosd supervises for the installed agent. It reads
//! `ETOS_URL`, `ETOS_KEY_FILE`, `ETOS_AGENT` and `ETOS_STATE_DIR` (and the optional
//! `ETOS_STATE_DIR/config.toml`), starts the companion ([`gamecore_studio::app::start`]),
//! reports ready and serves until the node's `shutdown` or SIGTERM/SIGINT. Logs go to stderr
//! (the node's agent log) through a redacting writer; the environment is never logged.

use std::io::IsTerminal;
use std::process::ExitCode;

use etos_sdk::{AgentOptions, Client};
use gamecore_studio::app;
use gamecore_studio::config::Config;
use gamecore_studio::redact::RedactingStderr;

#[tokio::main]
async fn main() -> ExitCode {
    let filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info"));
    tracing_subscriber::fmt()
        .with_env_filter(filter)
        .with_ansi(std::io::stderr().is_terminal())
        .with_writer(RedactingStderr)
        .init();
    match run().await {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            tracing::error!("gamecore-studio stopped: {e}");
            ExitCode::FAILURE
        }
    }
}

async fn run() -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let cfg = Config::from_env()?;
    let client = Client::from_env()?;
    tracing::info!(version = gamecore_studio::VERSION, agent = %cfg.agent, "starting");
    let running = app::start(cfg, client, AgentOptions::default()).await?;
    let served = tokio::select! {
        r = running.serve() => r,
        () = terminated() => Ok(()),
    };
    running.shutdown().await;
    served.map_err(Into::into)
}

/// SIGTERM or SIGINT.
async fn terminated() {
    use tokio::signal::unix::{SignalKind, signal};
    match (
        signal(SignalKind::terminate()),
        signal(SignalKind::interrupt()),
    ) {
        (Ok(mut term), Ok(mut int)) => {
            tokio::select! {
                _ = term.recv() => {}
                _ = int.recv() => {}
            }
        }
        _ => {
            tracing::warn!("no signal handlers; stopping only on the node's shutdown");
            std::future::pending::<()>().await;
        }
    }
}
