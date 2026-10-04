//! The HTTP client of the SDK routes (`/api/v1` on the node's API listener): bearer
//! authentication, per-request timeouts, and bounded retries with backoff for safe calls.

use std::sync::Arc;
use std::time::Duration;

use reqwest::Method;
use reqwest::header::{CONTENT_TYPE, HeaderValue, RETRY_AFTER};
use serde::Serialize;
use serde::de::DeserializeOwned;
use serde_json::{Map, Value};

use crate::error::{Error, Result};
use crate::util::seg;
use crate::wire::{ParamValue, QueryRequest, QueryResponse, Refusal};

/// The body of a request.
pub(crate) enum Body {
    /// No body.
    Empty,
    /// A JSON value.
    Json(Vec<u8>),
    /// Raw bytes of a media type.
    Bytes(String, Vec<u8>),
}

/// A connection to one node. Cheap to clone; clones share the connection pool.
#[derive(Clone)]
pub struct Client {
    inner: Arc<Inner>,
}

struct Inner {
    http: reqwest::Client,
    url: String,
    key: String,
    agent: Option<String>,
    retries: u32,
    backoff: Duration,
    timeout: Duration,
}

impl std::fmt::Debug for Client {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Client")
            .field("url", &self.inner.url)
            .field("agent", &self.inner.agent)
            .finish_non_exhaustive()
    }
}

impl Client {
    /// A client for the node at `url` (e.g. `http://127.0.0.1:7070`) with an app or agent key.
    pub fn new(url: &str, key: &str) -> Result<Client> {
        let url = url.trim_end_matches('/');
        if !(url.starts_with("http://") || url.starts_with("https://")) {
            return Err(Error::Invalid(format!(
                "the node URL {url:?} is not an http(s) URL"
            )));
        }
        let key = key.trim();
        if key.is_empty() {
            return Err(Error::Invalid(
                "a key is required: an app key, or the agent key the node wrote to ETOS_KEY_FILE"
                    .into(),
            ));
        }
        let http = reqwest::Client::builder()
            .build()
            .map_err(|e| Error::Invalid(format!("cannot build the HTTP client: {e}")))?;
        Ok(Client {
            inner: Arc::new(Inner {
                http,
                url: url.to_string(),
                key: key.to_string(),
                agent: None,
                retries: 4,
                backoff: Duration::from_millis(200),
                timeout: Duration::from_secs(30),
            }),
        })
    }

    /// The client of a supervised agent process: `ETOS_URL`, the key read from
    /// `ETOS_KEY_FILE`, and the agent's name from `ETOS_AGENT` when set.
    pub fn from_env() -> Result<Client> {
        let var = |name: &str| {
            std::env::var(name).map_err(|_| {
                Error::Invalid(format!(
                    "{name} is not set; the node sets it when it starts an installed agent"
                ))
            })
        };
        let url = var("ETOS_URL")?;
        let key_file = var("ETOS_KEY_FILE")?;
        let key = std::fs::read_to_string(&key_file)
            .map_err(|e| Error::Invalid(format!("cannot read the key file {key_file}: {e}")))?;
        let client = Client::new(&url, &key)?;
        Ok(match std::env::var("ETOS_AGENT") {
            Ok(agent) if !agent.is_empty() => client.with_agent(&agent),
            _ => client,
        })
    }

    fn with(&self, f: impl FnOnce(&mut Inner)) -> Client {
        let mut inner = Inner {
            http: self.inner.http.clone(),
            url: self.inner.url.clone(),
            key: self.inner.key.clone(),
            agent: self.inner.agent.clone(),
            retries: self.inner.retries,
            backoff: self.inner.backoff,
            timeout: self.inner.timeout,
        };
        f(&mut inner);
        Client {
            inner: Arc::new(inner),
        }
    }

    /// The same client, speaking for the agent `name` (its key's agent).
    pub fn with_agent(&self, name: &str) -> Client {
        self.with(|i| i.agent = Some(name.to_string()))
    }

    /// Retries of a safe request after a transport error, 408, 429 or 5xx (default 4).
    pub fn with_retries(&self, retries: u32) -> Client {
        self.with(|i| i.retries = retries)
    }

    /// First backoff delay, doubled per attempt (default 200 ms).
    pub fn with_backoff(&self, backoff: Duration) -> Client {
        self.with(|i| i.backoff = backoff)
    }

    /// Per-request timeout (default 30 s; long polls add their wait).
    pub fn with_timeout(&self, timeout: Duration) -> Client {
        self.with(|i| i.timeout = timeout)
    }

    /// Publish a caller-persisted trace batch and return per-trace acknowledgments.
    /// Persist producer, sequence and payload before calling; retire only accepted traces.
    pub async fn publish_trace_batch(
        &self,
        app: &str,
        batch: &crate::wire::TraceBatch,
    ) -> Result<crate::wire::TraceAck> {
        self.json(
            Method::POST,
            &format!("/bindings/{}/traces", seg(app)),
            Some(batch),
            true,
            Duration::ZERO,
        )
        .await
    }

    /// The node's base URL, without a trailing slash.
    pub fn url(&self) -> &str {
        &self.inner.url
    }

    /// The agent this client speaks for, when it is an agent's client.
    pub fn agent(&self) -> Option<&str> {
        self.inner.agent.as_deref()
    }

    pub(crate) fn agent_name(&self) -> Result<&str> {
        self.agent().ok_or_else(|| {
            Error::Invalid(
                "this client names no agent: set ETOS_AGENT or use Client::with_agent".into(),
            )
        })
    }

    /// `Authorization` header value.
    pub(crate) fn bearer(&self) -> String {
        format!("Bearer {}", self.inner.key)
    }

    /// Send JSON to `/api/v1<path>` and parse the JSON answer.
    pub(crate) async fn json<T: DeserializeOwned>(
        &self,
        method: Method,
        path: &str,
        body: Option<&(impl Serialize + ?Sized)>,
        safe: bool,
        extra_wait: Duration,
    ) -> Result<T> {
        let body = match body {
            Some(b) => Body::Json(
                serde_json::to_vec(b)
                    .map_err(|e| Error::Invalid(format!("cannot encode the request: {e}")))?,
            ),
            None => Body::Empty,
        };
        let bytes = self
            .send(method.clone(), path, body, safe, extra_wait)
            .await?;
        if bytes.is_empty() {
            return serde_json::from_slice(b"null").map_err(|_| {
                Error::Protocol(format!("{method} {path} answered with an empty body"))
            });
        }
        serde_json::from_slice(&bytes).map_err(|e| {
            Error::Protocol(format!(
                "{method} {path} answered with an unexpected body: {e}"
            ))
        })
    }

    /// Send a request and return the raw answer body of a 2xx response.
    pub(crate) async fn send(
        &self,
        method: Method,
        path: &str,
        body: Body,
        safe: bool,
        extra_wait: Duration,
    ) -> Result<Vec<u8>> {
        let attempts = if safe { self.inner.retries + 1 } else { 1 };
        let mut delay = self.inner.backoff;
        let mut attempt = 1;
        loop {
            match self.once(method.clone(), path, &body, extra_wait).await {
                Ok(bytes) => return Ok(bytes),
                Err((err, retry_after)) => {
                    if !err.is_retryable() || attempt >= attempts {
                        return Err(err);
                    }
                    tokio::time::sleep(delay.max(retry_after)).await;
                    delay = (delay * 2).min(Duration::from_secs(30));
                    attempt += 1;
                }
            }
        }
    }

    async fn once(
        &self,
        method: Method,
        path: &str,
        body: &Body,
        extra_wait: Duration,
    ) -> std::result::Result<Vec<u8>, (Error, Duration)> {
        let url = format!("{}/api/v1{path}", self.inner.url);
        let timeout = self.inner.timeout + extra_wait;
        let mut req = self
            .inner
            .http
            .request(method.clone(), &url)
            .timeout(timeout)
            .header(reqwest::header::AUTHORIZATION, self.bearer())
            .header(reqwest::header::ACCEPT, "application/json");
        match body {
            Body::Empty => {}
            Body::Json(bytes) => {
                req = req
                    .header(CONTENT_TYPE, HeaderValue::from_static("application/json"))
                    .body(bytes.clone());
            }
            Body::Bytes(media_type, bytes) => {
                req = req
                    .header(CONTENT_TYPE, media_type.as_str())
                    .body(bytes.clone());
            }
        }
        let res = req.send().await.map_err(|e| {
            let what = if e.is_timeout() {
                format!("{method} {path} timed out after {} ms", timeout.as_millis())
            } else {
                format!("cannot reach the node at {}: {e}", self.inner.url)
            };
            (Error::Transport(what), Duration::ZERO)
        })?;
        let status = res.status();
        let retry_after = res
            .headers()
            .get(RETRY_AFTER)
            .and_then(|v| v.to_str().ok())
            .and_then(|v| v.trim().parse::<u64>().ok())
            .map(|s| Duration::from_secs(s.min(60)))
            .unwrap_or(Duration::ZERO);
        let bytes = res.bytes().await.map_err(|e| {
            (
                Error::Transport(format!("{method} {path}: the answer was cut off: {e}")),
                Duration::ZERO,
            )
        })?;
        if status.is_success() {
            return Ok(bytes.to_vec());
        }
        Err((refusal_error(status.as_u16(), &bytes), retry_after))
    }

    /// SQL (or GraphQL) over the catalog within the key's scope. Read-only, so it is retried.
    pub async fn query(&self, query: &Query) -> Result<QueryResponse> {
        self.json(
            Method::POST,
            "/query",
            Some(&query.request),
            true,
            Duration::ZERO,
        )
        .await
    }

    /// Call a service method of an agent this app uses (`POST /services/{agent}/{method}`).
    /// `body` is the method's input (it may name the `user` the call is for); the answer is
    /// the method's result. Not retried: a method may change state.
    pub async fn call_service(&self, agent: &str, method: &str, body: &Value) -> Result<Value> {
        let path = format!("/services/{}/{}", seg(agent), seg(method));
        self.json(Method::POST, &path, Some(body), false, Duration::ZERO)
            .await
    }
}

/// Build the error of a non-2xx answer, falling back to a generic refusal for foreign bodies.
pub(crate) fn refusal_error(status: u16, body: &[u8]) -> Error {
    match serde_json::from_slice::<Refusal>(body) {
        Ok(r) => Error::Refused {
            status,
            code: r.code,
            message: r.message,
            hint: r.hint,
        },
        Err(_) => {
            let text = String::from_utf8_lossy(body);
            let text: String = text.chars().take(200).collect();
            Error::Refused {
                status,
                code: format!("http_{status}"),
                message: if text.is_empty() {
                    format!("HTTP {status}")
                } else {
                    text
                },
                hint: None,
            }
        }
    }
}

/// A query: SQL or GraphQL, with typed bind parameters for the `:name` placeholders.
#[derive(Debug, Clone)]
pub struct Query {
    request: QueryRequest,
}

impl Query {
    /// A SQL read.
    pub fn sql(sql: &str) -> Query {
        Query {
            request: QueryRequest {
                sql: Some(sql.to_string()),
                ..QueryRequest::default()
            },
        }
    }

    /// A GraphQL read with its variables.
    pub fn graphql(graphql: &str, variables: Map<String, Value>) -> Query {
        Query {
            request: QueryRequest {
                graphql: Some(graphql.to_string()),
                variables: Some(variables),
                ..QueryRequest::default()
            },
        }
    }

    /// Bind `:name` to a value: `true`, an integer, a number or a text (a time as RFC 3339).
    /// Values are bound as typed literals by the node, never spliced into the SQL.
    pub fn param(mut self, name: &str, value: impl Into<ParamValue>) -> Query {
        self.request.params.insert(name.to_string(), value.into());
        self
    }

    /// Narrow the scope to one user's partition.
    pub fn user(mut self, user: &str) -> Query {
        self.request.user = Some(user.to_string());
        self
    }

    /// Bypass the node's cache.
    pub fn fresh(mut self) -> Query {
        self.request.fresh = true;
        self
    }

    /// Continue a paged result.
    pub fn cursor(mut self, cursor: &str) -> Query {
        self.request.cursor = Some(cursor.to_string());
        self
    }

    /// For an agent: run the query as `app`, an app whose manifest `uses` the agent. The
    /// query sees exactly that app's tables, under their own names (`not_allowed` otherwise).
    pub fn app(mut self, app: &str) -> Query {
        self.request.app = Some(app.to_string());
        self
    }

    /// Plan the SQL without running it: the answer has the result `columns`, no rows, and a
    /// `plan` with the bind parameters and the tables read (their keys and columns).
    pub fn describe(mut self) -> Query {
        self.request.describe = true;
        self
    }

    /// The request as it travels.
    pub fn request(&self) -> &QueryRequest {
        &self.request
    }
}
