//! The agent's other routes (design-agents §5.5–5.6): operations, provider passthrough, the
//! change feed, tasks, topics and files.

use std::collections::VecDeque;
use std::time::Duration;

use futures::Stream;
use reqwest::Method;
use serde::de::DeserializeOwned;
use serde_json::Value;

use crate::client::{Body, Client};
use crate::error::{Error, Result};
use crate::util::seg;
use crate::wire::{
    ChangeEntry, ChangePage, FileInfo, TaskInfo, TaskRequest, TopicAck, TopicPage, TopicPost,
};

impl Client {
    /// The operations (`POST /ops/{op}`), charged to the agent's budget.
    pub fn ops(&self) -> Ops {
        Ops {
            client: self.clone(),
            task: None,
        }
    }

    /// The change feed of the tables the key may read, after position `after`, restricted to
    /// `tables` (all readable tables when empty).
    pub fn changes(&self, after: u64, tables: &[&str]) -> Changes {
        Changes {
            client: self.clone(),
            after,
            tables: tables.iter().map(|t| t.to_string()).collect(),
            app: None,
            wait: Duration::from_secs(25),
            queue: VecDeque::new(),
        }
    }

    /// Tasks for the workers the agent's manifest defines.
    pub fn tasks(&self) -> Tasks {
        Tasks {
            client: self.clone(),
        }
    }

    /// Topics the agent owns and the topics of its tasks.
    pub fn topics(&self) -> Topics {
        Topics {
            client: self.clone(),
        }
    }

    /// Files: bytes in, pinned references out.
    pub fn files(&self) -> Files {
        Files {
            client: self.clone(),
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Operations.

/// The etops operations. Inputs and outputs are the operations' own JSON (etops' README).
#[derive(Debug, Clone)]
pub struct Ops {
    client: Client,
    task: Option<String>,
}

impl Ops {
    /// Charge the operations to `task` instead of the agent (a tool call's
    /// `call.context().task`), while the task runs. Sent as `?task=`.
    pub fn for_task(mut self, task: &str) -> Ops {
        self.task = Some(task.to_string());
        self
    }

    /// Run the operation `op` with `input`. Not retried: an operation is charged.
    pub async fn call(&self, op: &str, input: Value) -> Result<Value> {
        if !input.is_object() {
            return Err(Error::Invalid(format!(
                "the input of {op} must be a JSON object"
            )));
        }
        let mut path = format!("/ops/{}", seg(op));
        if let Some(task) = &self.task {
            path.push_str(&format!("?task={}", seg(task)));
        }
        self.client
            .json(Method::POST, &path, Some(&input), false, Duration::ZERO)
            .await
    }

    /// A decision (`decide`).
    pub async fn decide(&self, input: Value) -> Result<Value> {
        self.call("decide", input).await
    }

    /// Rank candidates (`rank`).
    pub async fn rank(&self, input: Value) -> Result<Value> {
        self.call("rank", input).await
    }

    /// Choose among options (`choose`).
    pub async fn choose(&self, input: Value) -> Result<Value> {
        self.call("choose", input).await
    }

    /// Text from an image or a document (`ocr`).
    pub async fn ocr(&self, input: Value) -> Result<Value> {
        self.call("ocr", input).await
    }

    /// Text from speech (`transcribe`).
    pub async fn transcribe(&self, input: Value) -> Result<Value> {
        self.call("transcribe", input).await
    }

    /// A description of an image or a document (`describe`).
    pub async fn describe(&self, input: Value) -> Result<Value> {
        self.call("describe", input).await
    }

    /// Generated media (`generate`).
    pub async fn generate(&self, input: Value) -> Result<Value> {
        self.call("generate", input).await
    }

    /// Speech from text (`tts`).
    pub async fn tts(&self, input: Value) -> Result<Value> {
        self.call("tts", input).await
    }

    /// Web search (`search`).
    pub async fn search(&self, input: Value) -> Result<Value> {
        self.call("search", input).await
    }
}

// ---------------------------------------------------------------------------------------------
// Provider passthrough.

/// Provider passthrough: speak a provider's native protocol through the node, which
/// substitutes the credential, meters and rate-limits.
pub mod providers {
    use crate::client::Client;
    use crate::util::seg;

    /// Where to send a provider's native requests, and the header that authenticates them.
    #[derive(Clone, PartialEq, Eq)]
    pub struct ProviderUrl {
        /// `<node>/api/v1/providers/<provider>`; append the provider's own path.
        pub base_url: String,
        /// The header name (`Authorization`).
        pub header: String,
        /// Its value (`Bearer <agent key>`); the provider's credential never reaches the agent.
        pub value: String,
    }

    impl std::fmt::Debug for ProviderUrl {
        fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
            f.debug_struct("ProviderUrl")
                .field("base_url", &self.base_url)
                .field("header", &self.header)
                .finish_non_exhaustive()
        }
    }

    /// The passthrough of `provider` (a name from the node's `ops.toml`, or
    /// `model:<alias>` for an OpenAI-compatible model of `models.toml`). The agent's manifest
    /// must list it under `providers`.
    pub fn url(client: &Client, provider: &str) -> ProviderUrl {
        ProviderUrl {
            base_url: format!("{}/api/v1/providers/{}", client.url(), seg(provider)),
            header: "Authorization".into(),
            value: client.bearer(),
        }
    }
}

// ---------------------------------------------------------------------------------------------
// The change feed.

/// A position in the change feed, followed by long polls (`GET /changes`).
#[derive(Debug)]
pub struct Changes {
    client: Client,
    after: u64,
    tables: Vec<String>,
    app: Option<String>,
    wait: Duration,
    queue: VecDeque<ChangeEntry>,
}

impl Changes {
    /// The position after the last change returned: where the feed resumes.
    pub fn position(&self) -> u64 {
        self.after
    }

    /// How long one poll waits for changes (default 25 s; the node caps it).
    pub fn wait(mut self, wait: Duration) -> Changes {
        self.wait = wait;
        self
    }

    /// For an agent: only the changes of `app`, an app whose manifest `uses` the agent
    /// (`app=`).
    pub fn app(mut self, app: &str) -> Changes {
        self.app = Some(app.to_string());
        self
    }

    /// One long poll: the changes after the position (possibly none). Advances the position.
    pub async fn page(&mut self) -> Result<Vec<ChangeEntry>> {
        let mut path = format!(
            "/changes?after={}&wait_ms={}",
            self.after,
            self.wait.as_millis()
        );
        if !self.tables.is_empty() {
            let tables: Vec<String> = self.tables.iter().map(|t| seg(t.as_str())).collect();
            path.push_str(&format!("&tables={}", tables.join(",")));
        }
        if let Some(app) = &self.app {
            path.push_str(&format!("&app={}", seg(app)));
        }
        let page: ChangePage = self
            .client
            .json(Method::GET, &path, None::<&Value>, true, self.wait)
            .await?;
        self.after = page.next.max(self.after);
        Ok(page.changes)
    }

    /// The next change, waiting for one.
    pub async fn next_change(&mut self) -> Result<ChangeEntry> {
        loop {
            if let Some(c) = self.queue.pop_front() {
                return Ok(c);
            }
            let page = self.page().await?;
            self.queue.extend(page);
        }
    }

    /// The feed as a stream; it ends after the first error.
    pub fn into_stream(self) -> impl Stream<Item = Result<ChangeEntry>> {
        futures::stream::unfold(Some(self), |state| async move {
            let mut changes = state?;
            match changes.next_change().await {
                Ok(c) => Some((Ok(c), Some(changes))),
                Err(e) => Some((Err(e), None)),
            }
        })
    }
}

// ---------------------------------------------------------------------------------------------
// Tasks.

/// The task routes: tasks for the workers the agent's manifest defines.
#[derive(Debug, Clone)]
pub struct Tasks {
    client: Client,
}

impl Tasks {
    /// Cancel a task this agent opened; closes resources and advances the worker queue.
    pub async fn cancel(&self, id: &str) -> Result<TaskInfo> {
        self.client
            .json(
                Method::POST,
                &format!("/tasks/{}/cancel", seg(id)),
                Some(&serde_json::json!({})),
                true,
                Duration::ZERO,
            )
            .await
    }

    /// Open a task (`POST /tasks`). With an `id`, a repeat returns the same task, so the
    /// request is retried.
    pub async fn open(&self, task: &TaskRequest) -> Result<TaskInfo> {
        if task.worker.is_empty() || task.text.is_empty() {
            return Err(Error::Invalid("a task needs a worker and a text".into()));
        }
        self.client
            .json(
                Method::POST,
                "/tasks",
                Some(task),
                task.id.is_some(),
                Duration::ZERO,
            )
            .await
    }

    /// Read a task back (`GET /tasks/{id}`).
    pub async fn get(&self, id: &str) -> Result<TaskInfo> {
        let path = format!("/tasks/{}", seg(id));
        self.client
            .json(Method::GET, &path, None::<&Value>, true, Duration::ZERO)
            .await
    }
}

// ---------------------------------------------------------------------------------------------
// Topics.

/// The topic routes: the agent's own topics (`#agent/<agent>/<name>`) and its tasks' topics.
#[derive(Debug, Clone)]
pub struct Topics {
    client: Client,
}

impl Topics {
    /// Publish a record (`POST /topics/{topic}/records`). With an `id`, a repeat is not posted
    /// again, so the request is retried.
    pub async fn publish(&self, topic: &str, record: &TopicPost) -> Result<TopicAck> {
        let path = format!("/topics/{}/records", seg(topic));
        self.client
            .json(
                Method::POST,
                &path,
                Some(record),
                record.id.is_some(),
                Duration::ZERO,
            )
            .await
    }

    /// The records after position `after`, at most `limit`; waits up to `wait` for the first
    /// one (`GET /topics/{topic}/records`).
    pub async fn read(
        &self,
        topic: &str,
        after: u64,
        limit: u32,
        wait: Duration,
    ) -> Result<TopicPage> {
        let path = format!(
            "/topics/{}/records?after={after}&limit={limit}&wait_ms={}",
            seg(topic),
            wait.as_millis()
        );
        self.client
            .json(Method::GET, &path, None::<&Value>, true, wait)
            .await
    }
}

// ---------------------------------------------------------------------------------------------
// Files.

/// The file routes.
#[derive(Debug, Clone)]
pub struct Files {
    client: Client,
}

impl Files {
    /// Turn bytes into a pinned reference (`POST /files?name=&media_type=`, the bytes as the
    /// body) that tool results can carry.
    pub async fn put(&self, name: &str, media_type: &str, data: &[u8]) -> Result<FileInfo> {
        let path = format!("/files?name={}&media_type={}", seg(name), seg(media_type));
        let body = Body::Bytes(media_type.to_string(), data.to_vec());
        let bytes = self
            .client
            .send(Method::POST, &path, body, false, Duration::ZERO)
            .await?;
        parse(&bytes, "POST /files")
    }

    /// The bytes of a reference the agent may read (`GET /files/{ref}`).
    pub async fn get(&self, reference: &str) -> Result<Vec<u8>> {
        let path = format!("/files/{}", seg(reference));
        self.client
            .send(Method::GET, &path, Body::Empty, true, Duration::ZERO)
            .await
    }
}

fn parse<T: DeserializeOwned>(bytes: &[u8], what: &str) -> Result<T> {
    serde_json::from_slice(bytes)
        .map_err(|e| Error::Protocol(format!("{what} answered with an unexpected body: {e}")))
}
