//! Transaction-agent entrances: send a user's request, then follow the conversation's
//! progress and results.

use std::collections::VecDeque;
use std::time::Duration;

use base64::Engine;
use futures::Stream;
use reqwest::Method;
use serde_json::Value;

use crate::client::Client;
use crate::error::{Error, Result};
use crate::util::{new_id, seg};
use crate::wire::{
    AttachmentUpload, ConversationEvent, ConversationPage, EntranceAck, EntranceRequest,
};

/// A file sent with a request.
#[derive(Debug, Clone)]
pub struct Attachment {
    /// The file name.
    pub name: String,
    /// The media type, e.g. `text/plain`.
    pub media_type: String,
    /// The content.
    pub data: Vec<u8>,
}

/// A user request to an entrance.
#[derive(Debug, Clone, Default)]
pub struct Ask {
    /// The app's user who asks.
    pub user: String,
    /// The request text.
    pub text: String,
    /// Files sent with it.
    pub attachments: Vec<Attachment>,
    /// Continue this conversation instead of starting one.
    pub conversation: Option<String>,
    /// The request id (default: a fresh one). A repeat returns the original conversation.
    pub request: Option<String>,
}

/// An entrance of a transaction agent.
#[derive(Debug, Clone)]
pub struct Entrance {
    client: Client,
    name: String,
}

impl Client {
    /// The entrance `name`.
    pub fn entrance(&self, name: &str) -> Entrance {
        Entrance {
            client: self.clone(),
            name: name.to_string(),
        }
    }
}

impl Entrance {
    /// Send a request; returns the conversation id. Idempotent on the request id, so it is
    /// retried.
    pub async fn ask(&self, ask: Ask) -> Result<String> {
        if ask.text.is_empty() {
            return Err(Error::Invalid("the request text is empty".into()));
        }
        if ask.user.is_empty() {
            return Err(Error::Invalid("the request names no user".into()));
        }
        let body = EntranceRequest {
            request: ask.request.unwrap_or_else(|| new_id("req")),
            user: ask.user,
            text: ask.text,
            attachments: ask
                .attachments
                .into_iter()
                .map(|a| AttachmentUpload {
                    name: a.name,
                    media_type: a.media_type,
                    data: base64::engine::general_purpose::STANDARD.encode(&a.data),
                })
                .collect(),
            conversation: ask.conversation,
        };
        let path = format!("/entrances/{}/requests", seg(&self.name));
        let ack: EntranceAck = self
            .client
            .json(Method::POST, &path, Some(&body), true, Duration::ZERO)
            .await?;
        Ok(ack.conversation)
    }

    /// A conversation of this entrance.
    pub fn conversation(&self, id: &str) -> Conversation {
        Conversation {
            client: self.client.clone(),
            path: format!("/entrances/{}/conversations/{}", seg(&self.name), seg(id)),
        }
    }
}

/// One conversation.
#[derive(Debug, Clone)]
pub struct Conversation {
    client: Client,
    path: String,
}

impl Conversation {
    /// One long poll: the events after position `after` (waiting up to `wait`).
    pub async fn page(&self, after: u64, wait: Duration) -> Result<ConversationPage> {
        let path = format!("{}?after={after}&wait_ms={}", self.path, wait.as_millis());
        self.client
            .json(Method::GET, &path, None::<&Value>, true, wait)
            .await
    }

    /// Every event after position `after`, each once and in order, until the conversation is
    /// done.
    pub fn events(&self, after: u64) -> impl Stream<Item = Result<ConversationEvent>> + use<> {
        struct State {
            conv: Conversation,
            after: u64,
            queue: VecDeque<ConversationEvent>,
            done: bool,
        }
        let state = State {
            conv: self.clone(),
            after,
            queue: VecDeque::new(),
            done: false,
        };
        futures::stream::unfold(state, |mut s| async move {
            loop {
                if let Some(e) = s.queue.pop_front() {
                    return Some((Ok(e), s));
                }
                if s.done {
                    return None;
                }
                match s.conv.page(s.after, Duration::from_secs(25)).await {
                    Ok(page) => {
                        for e in page.events {
                            if e.pos() > s.after {
                                s.after = e.pos();
                                s.queue.push_back(e);
                            }
                        }
                        s.done = page.done;
                    }
                    Err(e) => {
                        s.done = true;
                        return Some((Err(e), s));
                    }
                }
            }
        })
    }
}
