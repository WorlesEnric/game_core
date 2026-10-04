//! The agent surface: the channel the node pushes work over, tools for workers, services for
//! apps, and the proxied-connection endpoint (design-agents §5.1–5.4).
//!
//! ```no_run
//! use etos_sdk::{Agent, Client, Reply, ToolSpec};
//!
//! # async fn run() -> etos_sdk::Result<()> {
//! let agent = Agent::connect(Client::from_env()?).await?;
//! agent
//!     .tool("echo", ToolSpec::new("Repeat the text"), |call| async move {
//!         call.event("echoing", serde_json::json!({}));
//!         Ok(Reply::text(call.args()["text"].as_str().unwrap_or_default()))
//!     })
//!     .await?;
//! agent.serve().await
//! # }
//! ```

mod cache;
mod channel;

use std::collections::{BTreeMap, HashMap};
use std::future::Future;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, RwLock};
use std::time::Duration;

use futures::FutureExt;
use futures::future::BoxFuture;
use reqwest::Method;
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use tokio::sync::{mpsc, watch};

use crate::client::Client;
use crate::error::{Error, Refusal, Result};
use crate::util::seg;
use crate::wire::{EndpointAck, EndpointRequest, Retry, ServiceAck, ToolAck};

use cache::ResultCache;

/// `etos-sdk/rust/<version>`, sent in the channel's `hello`.
pub const SDK: &str = concat!("etos-sdk/rust/", env!("CARGO_PKG_VERSION"));

/// How a tool is registered (`PUT /tools/{agent}.{name}`).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ToolSpec {
    /// What the tool does, for the model.
    pub description: String,
    /// JSON Schema of the arguments.
    pub input_schema: Value,
    /// Whether a call may be delivered again.
    pub retry: Retry,
    /// Longest time one call may take, in seconds.
    pub timeout_s: u64,
}

impl ToolSpec {
    /// A tool taking any object, `retry: never`, 60 s timeout.
    pub fn new(description: &str) -> ToolSpec {
        ToolSpec {
            description: description.to_string(),
            input_schema: json!({"type": "object"}),
            retry: Retry::Never,
            timeout_s: 60,
        }
    }

    /// The arguments' JSON Schema.
    pub fn input_schema(mut self, schema: Value) -> ToolSpec {
        self.input_schema = schema;
        self
    }

    /// Whether a call may be delivered again.
    pub fn retry(mut self, retry: Retry) -> ToolSpec {
        self.retry = retry;
        self
    }

    /// The timeout of one call.
    pub fn timeout_s(mut self, seconds: u64) -> ToolSpec {
        self.timeout_s = seconds;
        self
    }
}

/// How a service method is declared (`PUT /services/{agent}`).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ServiceSpec {
    /// JSON Schema of the input.
    pub input_schema: Value,
    /// The node answers `deadline` (504) after this long.
    pub deadline_ms: u64,
    /// The apps allowed to call it; absent means every app that uses the agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub apps: Option<Vec<String>>,
}

impl ServiceSpec {
    /// A method taking any object, with a 5 s deadline, open to every app that uses the agent.
    pub fn new() -> ServiceSpec {
        ServiceSpec {
            input_schema: json!({"type": "object"}),
            deadline_ms: 5_000,
            apps: None,
        }
    }

    /// The input's JSON Schema.
    pub fn input_schema(mut self, schema: Value) -> ServiceSpec {
        self.input_schema = schema;
        self
    }

    /// The deadline.
    pub fn deadline_ms(mut self, ms: u64) -> ServiceSpec {
        self.deadline_ms = ms;
        self
    }

    /// Only these apps may call it.
    pub fn apps(mut self, apps: &[&str]) -> ServiceSpec {
        self.apps = Some(apps.iter().map(|a| a.to_string()).collect());
        self
    }
}

impl Default for ServiceSpec {
    fn default() -> ServiceSpec {
        ServiceSpec::new()
    }
}

/// Whether a call is a worker's tool call or an app's service call.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum CallKind {
    /// A tool call of a worker (an effect of its task).
    Tool,
    /// A service call of an app.
    Service,
}

/// Who a call is for.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct CallContext {
    /// The task, for a tool call.
    #[serde(default)]
    pub task: Option<String>,
    /// The worker, for a tool call.
    #[serde(default)]
    pub worker: Option<String>,
    /// The calling app, for a service call.
    #[serde(default)]
    pub app: Option<String>,
    /// The user the app names, for a service call: scope reads to that user's partition.
    #[serde(default)]
    pub user: Option<String>,
}

/// A handler's answer.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Reply {
    /// Text for the model (a tool) or the app.
    pub text: Option<String>,
    /// File references (from `files().put`) the result carries.
    pub refs: Vec<String>,
    /// Structured data; a service call's response body.
    pub data: Option<Value>,
}

impl Reply {
    /// A reply with text.
    pub fn text(text: &str) -> Reply {
        Reply {
            text: Some(text.to_string()),
            ..Reply::default()
        }
    }

    /// A reply with data (a service's response body).
    pub fn data(data: Value) -> Reply {
        Reply {
            data: Some(data),
            ..Reply::default()
        }
    }

    /// The same reply carrying file references.
    pub fn with_refs(mut self, refs: Vec<String>) -> Reply {
        self.refs = refs;
        self
    }

    /// The same reply with data as well.
    pub fn with_data(mut self, data: Value) -> Reply {
        self.data = Some(data);
        self
    }
}

/// One call the node pushed: a tool call or a service call.
#[derive(Clone)]
pub struct Call {
    id: String,
    kind: CallKind,
    name: String,
    args: Value,
    context: CallContext,
    deadline: Option<tokio::time::Instant>,
    cancel: watch::Receiver<bool>,
    inner: Arc<Inner>,
}

impl std::fmt::Debug for Call {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Call")
            .field("id", &self.id)
            .field("kind", &self.kind)
            .field("name", &self.name)
            .field("context", &self.context)
            .finish_non_exhaustive()
    }
}

impl Call {
    /// The call id: the effect key of a tool call. A redelivered call keeps it.
    pub fn id(&self) -> &str {
        &self.id
    }

    /// Tool or service.
    pub fn kind(&self) -> CallKind {
        self.kind
    }

    /// The tool's or method's short name.
    pub fn name(&self) -> &str {
        &self.name
    }

    /// The arguments (a tool) or the input (a service).
    pub fn args(&self) -> &Value {
        &self.args
    }

    /// The arguments as a type; a mismatch is refused with `invalid_args`.
    pub fn args_as<T: DeserializeOwned>(&self) -> std::result::Result<T, Refusal> {
        T::deserialize(&self.args).map_err(|e| {
            Refusal::new(
                "invalid_args",
                format!("the arguments of {} do not fit: {e}", self.name),
            )
        })
    }

    /// Task, worker, app and user.
    pub fn context(&self) -> &CallContext {
        &self.context
    }

    /// When the node stops waiting, if it said. The call is cancelled at that time.
    pub fn deadline(&self) -> Option<tokio::time::Instant> {
        self.deadline
    }

    /// Report progress: `{status, detail}` goes to the task's topic and the worker's trace.
    /// Best-effort: dropped while the channel is down.
    pub fn event(&self, status: &str, detail: Value) {
        let msg = json!({"type": "event", "id": self.id, "status": status, "detail": detail});
        self.inner.send(msg.to_string());
    }

    /// Resolves when the node cancels the call (the task was cancelled, the turn stopped, or
    /// the deadline passed). A handler should stop soon after and answer.
    pub async fn cancelled(&self) {
        let mut rx = self.cancel.clone();
        if rx.wait_for(|c| *c).await.is_err() {
            // The call is finished; nothing will cancel it any more.
            futures::future::pending::<()>().await;
        }
    }

    /// Whether the call was cancelled.
    pub fn is_cancelled(&self) -> bool {
        *self.cancel.borrow()
    }
}

type Handler =
    Arc<dyn Fn(Call) -> BoxFuture<'static, std::result::Result<Reply, Refusal>> + Send + Sync>;

fn boxed<F, Fut>(handler: F) -> Handler
where
    F: Fn(Call) -> Fut + Send + Sync + 'static,
    Fut: Future<Output = std::result::Result<Reply, Refusal>> + Send + 'static,
{
    Arc::new(move |call| handler(call).boxed())
}

/// What the node said when it accepted the agent's channel (its `welcome`).
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct Welcome {
    /// The agent, as the node knows it.
    pub agent: String,
    /// The node's name.
    pub node: String,
    /// The SDK API version the node serves (`1.0.0`).
    pub sdk: String,
    /// The token the node's proxy adds to requests for the agent's endpoint.
    #[serde(rename = "proxy_token")]
    token: String,
}

impl Welcome {
    /// The proxy token: trust only proxied requests that carry it.
    pub fn proxy_token(&self) -> ProxyToken {
        ProxyToken(self.token.clone())
    }
}

/// Tuning of the channel.
#[derive(Debug, Clone)]
pub struct AgentOptions {
    /// First reconnect delay, doubled per failure (default 250 ms).
    pub reconnect_min: Duration,
    /// Longest reconnect delay (default 30 s).
    pub reconnect_max: Duration,
    /// A connection with no message from the node for this long is dropped and reopened
    /// (default 60 s; the node pings every 20 s).
    pub idle_timeout: Duration,
    /// Results kept for redelivered calls (default 4096).
    pub cache_size: usize,
    /// How long a result is kept (default 15 min).
    pub cache_ttl: Duration,
}

impl Default for AgentOptions {
    fn default() -> AgentOptions {
        AgentOptions {
            reconnect_min: Duration::from_millis(250),
            reconnect_max: Duration::from_secs(30),
            idle_timeout: Duration::from_secs(60),
            cache_size: 4096,
            cache_ttl: Duration::from_secs(15 * 60),
        }
    }
}

/// Where the channel stands.
#[derive(Debug, Clone, PartialEq)]
enum Status {
    Running,
    /// The node asked the agent to shut down.
    Shutdown,
    /// Closed locally.
    Closed,
    /// The node refused the agent (a revoked key, an unsupported SDK version).
    Failed(Error),
}

pub(crate) struct Inner {
    client: Client,
    name: String,
    options: AgentOptions,
    tools: RwLock<HashMap<String, (ToolSpec, Handler)>>,
    services: RwLock<BTreeMap<String, (ServiceSpec, Handler)>>,
    endpoint_url: RwLock<Option<String>>,
    /// Calls being handled: their cancel switches.
    running: Mutex<HashMap<String, Arc<watch::Sender<bool>>>>,
    cache: Mutex<ResultCache>,
    /// The current connection's outgoing queue.
    outbox: Mutex<Option<mpsc::UnboundedSender<String>>>,
    connected: watch::Sender<bool>,
    /// The node's latest `welcome`.
    welcome: RwLock<Option<Welcome>>,
    ready: AtomicBool,
    status: watch::Sender<Status>,
}

impl Inner {
    /// Queue a message on the current connection; false when there is none.
    fn send(&self, msg: String) -> bool {
        match self.outbox.lock() {
            Ok(o) => o.as_ref().is_some_and(|tx| tx.send(msg).is_ok()),
            Err(_) => false,
        }
    }

    fn stopping(&self) -> bool {
        *self.status.borrow() != Status::Running
    }
}

/// A connected agent. Cheap to clone.
#[derive(Clone)]
pub struct Agent {
    inner: Arc<Inner>,
}

impl std::fmt::Debug for Agent {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Agent")
            .field("name", &self.inner.name)
            .finish_non_exhaustive()
    }
}

impl Agent {
    /// Open the agent channel (`GET /api/v1/agent/connect`), say hello and wait for the node's
    /// welcome. The client must name the agent (`ETOS_AGENT`, or [`Client::with_agent`]).
    /// Refused keys, names and SDK versions surface here; later drops are reconnected in the
    /// background with backoff.
    pub async fn connect(client: Client) -> Result<Agent> {
        Agent::connect_with(client, AgentOptions::default()).await
    }

    /// [`Agent::connect`] with tuning.
    pub async fn connect_with(client: Client, options: AgentOptions) -> Result<Agent> {
        let name = client.agent_name()?.to_string();
        let (status, _) = watch::channel(Status::Running);
        let (connected, _) = watch::channel(false);
        let inner = Arc::new(Inner {
            cache: Mutex::new(ResultCache::new(options.cache_size, options.cache_ttl)),
            client,
            name,
            options,
            tools: RwLock::new(HashMap::new()),
            services: RwLock::new(BTreeMap::new()),
            endpoint_url: RwLock::new(None),
            running: Mutex::new(HashMap::new()),
            outbox: Mutex::new(None),
            connected,
            welcome: RwLock::new(None),
            ready: AtomicBool::new(false),
            status,
        });
        let ws = channel::open(&inner).await?;
        tokio::spawn(channel::run(inner.clone(), ws));
        Ok(Agent { inner })
    }

    /// The agent's name.
    pub fn name(&self) -> &str {
        &self.inner.name
    }

    /// The agent's client, for queries, operations and the other SDK routes.
    pub fn client(&self) -> &Client {
        &self.inner.client
    }

    /// Whether the channel is connected now.
    pub fn is_connected(&self) -> bool {
        *self.inner.connected.borrow()
    }

    /// The node's welcome on the current (or last) connection: its name, its SDK version and
    /// the proxy token.
    pub fn welcome(&self) -> Option<Welcome> {
        self.inner.welcome.read().ok().and_then(|w| w.clone())
    }

    /// Register the tool `<agent>.<name>` and handle its calls. Workers whose definition lists
    /// it can call it; the model sees it as `name`.
    pub async fn tool<F, Fut>(&self, name: &str, spec: ToolSpec, handler: F) -> Result<()>
    where
        F: Fn(Call) -> Fut + Send + Sync + 'static,
        Fut: Future<Output = std::result::Result<Reply, Refusal>> + Send + 'static,
    {
        lock_write(&self.inner.tools)?.insert(name.to_string(), (spec.clone(), boxed(handler)));
        let registered = register_tool(&self.inner, name, &spec).await;
        if registered.is_err() {
            lock_write(&self.inner.tools)?.remove(name);
        }
        registered
    }

    /// Unregister the tool (`DELETE /tools/<agent>.<name>`); its calls are refused from then on.
    pub async fn remove_tool(&self, name: &str) -> Result<()> {
        let path = format!("/tools/{}.{}", seg(&self.inner.name), seg(name));
        let _: ToolAck = self
            .inner
            .client
            .json(Method::DELETE, &path, None::<&Value>, true, Duration::ZERO)
            .await?;
        lock_write(&self.inner.tools)?.remove(name);
        Ok(())
    }

    /// Declare the service method `method` and handle its calls. Apps whose manifest uses
    /// this agent call it with `POST /services/<agent>/<method>`; the reply's `data` is the
    /// response body.
    pub async fn service<F, Fut>(&self, method: &str, spec: ServiceSpec, handler: F) -> Result<()>
    where
        F: Fn(Call) -> Fut + Send + Sync + 'static,
        Fut: Future<Output = std::result::Result<Reply, Refusal>> + Send + 'static,
    {
        lock_write(&self.inner.services)?.insert(method.to_string(), (spec, boxed(handler)));
        let registered = register_services(&self.inner).await;
        if registered.is_err() {
            lock_write(&self.inner.services)?.remove(method);
        }
        registered
    }

    /// Register the loopback listener apps reach through the node's proxy
    /// (`/api/v1/agents/<agent>/http/...`; needs the `proxy` grant). Returns the token the
    /// node adds to every proxied request (the same as the welcome's); trust only requests
    /// that carry it.
    pub async fn endpoint(&self, url: &str) -> Result<ProxyToken> {
        let ack: EndpointAck = self
            .inner
            .client
            .json(
                Method::PUT,
                "/agent/endpoint",
                Some(&EndpointRequest {
                    url: url.to_string(),
                }),
                true,
                Duration::ZERO,
            )
            .await?;
        *lock_write(&self.inner.endpoint_url)? = Some(url.to_string());
        Ok(ProxyToken(ack.proxy_token))
    }

    /// Report the agent ready and serve calls until the node asks it to shut down (then
    /// `Ok`), or refuses it (then the refusal). Tools and services registered before this
    /// are served from the first call on.
    pub async fn serve(&self) -> Result<()> {
        self.inner.ready.store(true, Ordering::SeqCst);
        self.inner.send(json!({"type": "ready"}).to_string());
        let mut rx = self.inner.status.subscribe();
        let status = match rx.wait_for(|s| *s != Status::Running).await {
            Ok(s) => s.clone(),
            Err(_) => Status::Closed,
        };
        match status {
            Status::Failed(e) => Err(e),
            _ => Ok(()),
        }
    }

    /// Close the channel and stop reconnecting; calls in flight are cancelled.
    pub fn close(&self) {
        let _ = self.inner.status.send_replace(Status::Closed);
        channel::cancel_all(&self.inner);
    }
}

fn lock_write<T>(lock: &RwLock<T>) -> Result<std::sync::RwLockWriteGuard<'_, T>> {
    lock.write()
        .map_err(|_| Error::Invalid("a handler table is unusable after a panic".into()))
}

async fn register_tool(inner: &Inner, name: &str, spec: &ToolSpec) -> Result<()> {
    let path = format!("/tools/{}.{}", seg(&inner.name), seg(name));
    let _: ToolAck = inner
        .client
        .json(Method::PUT, &path, Some(spec), true, Duration::ZERO)
        .await?;
    Ok(())
}

async fn register_services(inner: &Inner) -> Result<()> {
    let methods: BTreeMap<String, ServiceSpec> = match inner.services.read() {
        Ok(s) => s
            .iter()
            .map(|(k, (spec, _))| (k.clone(), spec.clone()))
            .collect(),
        Err(_) => return Err(Error::Invalid("the service table is unusable".into())),
    };
    let path = format!("/services/{}", seg(&inner.name));
    let _: ServiceAck = inner
        .client
        .json(
            Method::PUT,
            &path,
            Some(&json!({ "methods": methods })),
            true,
            Duration::ZERO,
        )
        .await?;
    Ok(())
}

/// The token the node's proxy adds to requests it passes to the agent's endpoint.
#[derive(Clone, PartialEq, Eq)]
pub struct ProxyToken(String);

impl std::fmt::Debug for ProxyToken {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("ProxyToken(..)")
    }
}

impl ProxyToken {
    /// The header carrying the token.
    pub const HEADER: &'static str = "x-etos-proxy-token";
    /// The header naming the calling app.
    pub const APP_HEADER: &'static str = "x-etos-app";

    /// The token itself.
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// Whether a request's token header value is this token (compared in constant time).
    pub fn check(&self, value: Option<&str>) -> bool {
        let Some(v) = value else { return false };
        let (a, b) = (self.0.as_bytes(), v.as_bytes());
        a.len() == b.len() && a.iter().zip(b).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
    }

    /// Check a request's headers; returns the calling app when the token is right.
    pub fn check_headers(&self, headers: &reqwest::header::HeaderMap) -> Option<String> {
        let token = headers.get(Self::HEADER).and_then(|v| v.to_str().ok());
        if !self.check(token) {
            return None;
        }
        Some(
            headers
                .get(Self::APP_HEADER)
                .and_then(|v| v.to_str().ok())
                .unwrap_or_default()
                .to_string(),
        )
    }
}
