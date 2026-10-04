//! Concurrent realtime media over ETOS. The SDK never connects directly to a vendor.
use crate::util::seg;
use crate::{Client, Error, Result};
use base64::{Engine as _, engine::general_purpose::STANDARD};
use futures::{
    SinkExt, StreamExt,
    stream::{SplitSink, SplitStream},
};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeSet;
use std::sync::Arc;
use std::time::Duration;
use tokio::net::TcpStream;
use tokio::sync::Mutex;
use tokio_tungstenite::{
    MaybeTlsStream, WebSocketStream,
    tungstenite::{Message, client::IntoClientRequest},
};

/// A feature an adapter actually implements, negotiated before opening a call.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Capability {
    /// Concurrent audio input and output.
    DuplexAudio,
    /// Explicit response creation, never automatic speech at end of user input.
    ManualResponse,
    /// Replace the bounded semantic context before a response.
    ContextReplace,
    /// Stop a response in progress.
    ResponseCancel,
    /// Remove unheard assistant audio from conversational context.
    PlaybackTruncate,
    /// User speech boundary notifications during assistant speech.
    SpeechDetection,
    /// Incremental and final user transcripts.
    InputTranscript,
}

/// Settings for one live provider connection; reconnecting requires a new generation.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct RealtimeConfig {
    /// Application session identity.
    pub session_id: String,
    /// Monotonic application generation, echoed on every event.
    pub generation: u64,
    /// Base system instructions.
    pub instructions: String,
    /// Provider-specific voice identifier.
    #[serde(default)]
    pub voice: Option<String>,
    /// Initially `pcm16`: signed, little-endian, mono PCM.
    pub audio_format: String,
    /// Negotiated sampling rate in Hz.
    pub sample_rate_hz: u32,
    /// Required capabilities; an unsupported session is refused before opening upstream.
    #[serde(default)]
    pub required: BTreeSet<Capability>,
}

/// A command with stable identity and generation fencing. Commands are not automatically
/// retried across disconnections: model execution may have started before the disconnect.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Command {
    /// Unique within this connection. A different payload under the same id is refused.
    pub id: String,
    /// Must match the opened session.
    pub session_id: String,
    /// Must match the opened generation.
    pub generation: u64,
    /// Control or media payload.
    pub action: Action,
}

/// Provider-neutral commands. Context and response commands are ordered by the node; an
/// adapter must wait for upstream application before returning from `send` for controls.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case", deny_unknown_fields)]
pub enum Action {
    /// Append an input chunk; sequence starts at zero and increments by one.
    InputAudio {
        /// Input sequence number.
        sequence: u64,
        /// Base64-encoded PCM16 bytes, at most 32 KiB decoded.
        audio: String,
    },
    /// Add a typed user utterance without creating a response.
    InputText {
        /// Utterance identity.
        item_id: String,
        /// User content.
        text: String,
    },
    /// Commit the current user utterance without automatically responding.
    InputCommit {
        /// Utterance identity.
        item_id: String,
    },
    /// Discard uncommitted microphone input.
    InputClear,
    /// Replace semantic context; no hidden model reasoning belongs in this payload.
    ContextReplace {
        /// Monotonically increasing context snapshot version.
        version: u64,
        /// Bounded system/user/context snapshot, interpreted by the provider adapter.
        content: Value,
    },
    /// Explicitly produce one response from the acknowledged snapshot.
    ResponseCreate {
        /// Unique response identity.
        response_id: String,
        /// Exact context version to use.
        context_version: u64,
    },
    /// Cancel output; late events can still arrive and must be discarded by clients.
    ResponseCancel {
        /// Response being interrupted.
        response_id: String,
    },
    /// Trim unheard audio using the client's actual playback cursor.
    PlaybackTruncate {
        /// Response identity.
        response_id: String,
        /// Provider-mapped conversation item.
        item_id: String,
        /// Audio stream incarnation.
        audio_epoch: u64,
        /// Milliseconds actually played for this item.
        audio_end_ms: u64,
    },
    /// Close this connection and release its resources.
    Close,
}

/// An event payload. Audio/transcript offsets are per item, never wall-clock guesses.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum RealtimeEvent {
    /// Negotiated connection settings.
    Ready {
        /// Actual adapter capabilities.
        capabilities: BTreeSet<Capability>,
        /// Agreed audio format.
        audio_format: String,
        /// Agreed sampling rate.
        sample_rate_hz: u32,
    },
    /// The adapter confirmed this control has taken effect. Emitted by the node after send.
    ControlApplied {
        /// Command identity.
        command_id: String,
    },
    /// Synthesized audio. Application playback must fence all fields before enqueueing.
    Audio {
        /// Response identity.
        response_id: String,
        /// Conversation item identity.
        item_id: String,
        /// Audio stream incarnation.
        audio_epoch: u64,
        /// Output sequence number for this item.
        sequence: u64,
        /// Start sample offset for this chunk.
        start_sample: u64,
        /// Number of mono samples in this chunk.
        sample_count: u32,
        /// Base64 PCM16 bytes.
        audio: String,
    },
    /// Full transcript revision, replacing earlier revisions of the same item.
    Transcript {
        /// User or assistant.
        role: String,
        /// Utterance identity.
        item_id: String,
        /// Present for assistant output.
        response_id: Option<String>,
        /// Revision number.
        revision: u64,
        /// Transcript so far (not a delta).
        text: String,
        /// True for the final revision.
        done: bool,
    },
    /// Input activity, including while the assistant is speaking.
    SpeechStarted {
        /// Utterance identity.
        item_id: String,
    },
    /// Input activity ended; this does not authorize model speech.
    SpeechEnded {
        /// Utterance identity.
        item_id: String,
    },
    /// Response generation ended; playback may still have queued audio.
    ResponseEnded {
        /// Response identity.
        response_id: String,
        /// Completed, cancelled or failed.
        status: String,
    },
    /// Usage delta since the previous event, including audio costs at configured rates.
    Usage {
        /// Metered usage.
        usage: Value,
    },
    /// Non-secret structured refusal.
    Error {
        /// Stable error code.
        code: String,
        /// Human-readable description.
        message: String,
    },
    /// The connection ended.
    Closed {
        /// End reason.
        reason: String,
    },
}

/// Every event carries the session and generation for client-side stale output rejection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EventFrame {
    /// Application session.
    pub session_id: String,
    /// Application generation.
    pub generation: u64,
    /// Event payload.
    pub event: RealtimeEvent,
}

type Socket = WebSocketStream<MaybeTlsStream<TcpStream>>;

/// Opens manually driven realtime sessions through the node.
#[derive(Debug, Clone)]
pub struct Realtime {
    client: Client,
}

impl Client {
    /// Provider-neutral voice transport; requires the realtime grant and provider allowlist.
    pub fn realtime(&self) -> Realtime {
        Realtime {
            client: self.clone(),
        }
    }
}

/// Independently owned, serial command writer. Event reads never lock this sender.
#[derive(Clone)]
pub struct RealtimeSender {
    sink: Arc<Mutex<SplitSink<Socket, Message>>>,
    session_id: String,
    generation: u64,
}

/// The event stream of one connection. A reconnect needs a new application generation.
pub struct RealtimeEvents {
    stream: SplitStream<Socket>,
    session_id: String,
    generation: u64,
    /// Negotiated settings from the node's initial ready frame.
    pub ready: EventFrame,
}

impl Realtime {
    /// Open and wait for capability negotiation. No upstream or response retries are made.
    pub async fn open(
        &self,
        provider: &str,
        config: &RealtimeConfig,
    ) -> Result<(RealtimeSender, RealtimeEvents)> {
        let url = format!(
            "{}/api/v1/realtime/connect?provider={}",
            self.client.url().replacen("http", "ws", 1),
            seg(provider)
        );
        let mut request = url
            .into_client_request()
            .map_err(|e| Error::Invalid(e.to_string()))?;
        request.headers_mut().insert(
            "authorization",
            self.client
                .bearer()
                .parse()
                .map_err(|_| Error::Invalid("invalid authorization header".into()))?,
        );
        let (mut socket, _) = tokio::time::timeout(
            Duration::from_secs(10),
            tokio_tungstenite::connect_async(request),
        )
        .await
        .map_err(|_| Error::Protocol("realtime connection deadline expired".into()))?
        .map_err(|e| Error::Protocol(format!("realtime connection failed: {e}")))?;
        socket
            .send(Message::Text(
                serde_json::to_string(config)
                    .map_err(|e| Error::Invalid(e.to_string()))?
                    .into(),
            ))
            .await
            .map_err(|e| Error::Protocol(format!("realtime config write failed: {e}")))?;
        let message = tokio::time::timeout(Duration::from_secs(20), socket.next())
            .await
            .map_err(|_| Error::Protocol("realtime negotiation deadline expired".into()))?
            .ok_or_else(|| Error::Protocol("realtime connection closed before ready".into()))?
            .map_err(|e| Error::Protocol(e.to_string()))?;
        let text = message
            .to_text()
            .map_err(|e| Error::Protocol(e.to_string()))?;
        let ready: EventFrame = serde_json::from_str(text)
            .map_err(|_| Error::Protocol(format!("realtime session refused: {text}")))?;
        if ready.session_id != config.session_id
            || ready.generation != config.generation
            || !matches!(ready.event, RealtimeEvent::Ready { .. })
        {
            return Err(Error::Protocol(
                "realtime ready frame does not match the requested generation".into(),
            ));
        }
        let (sink, stream) = socket.split();
        Ok((
            RealtimeSender {
                sink: Arc::new(Mutex::new(sink)),
                session_id: config.session_id.clone(),
                generation: config.generation,
            },
            RealtimeEvents {
                stream,
                session_id: config.session_id.clone(),
                generation: config.generation,
                ready,
            },
        ))
    }
}

impl RealtimeSender {
    /// Write a control. Await the matching ControlApplied event before depending on it.
    pub async fn command(&self, id: &str, action: Action) -> Result<()> {
        let command = Command {
            id: id.to_string(),
            session_id: self.session_id.clone(),
            generation: self.generation,
            action,
        };
        let text = serde_json::to_string(&command).map_err(|e| Error::Invalid(e.to_string()))?;
        tokio::time::timeout(Duration::from_secs(5), async {
            self.sink
                .lock()
                .await
                .send(Message::Text(text.into()))
                .await
        })
        .await
        .map_err(|_| Error::Protocol("realtime write deadline expired; outcome unknown".into()))?
        .map_err(|e| Error::Protocol(format!("realtime write failed: {e}")))
    }
    /// Send mono PCM16 input. Sequence starts at zero and increases by one per chunk.
    pub async fn audio(&self, sequence: u64, bytes: &[u8]) -> Result<()> {
        if bytes.is_empty() || bytes.len() > 32 * 1024 || bytes.len() % 2 != 0 {
            return Err(Error::Invalid(
                "realtime input must be 1–16384 PCM16 samples".into(),
            ));
        }
        self.command(
            &format!("audio-{sequence}"),
            Action::InputAudio {
                sequence,
                audio: STANDARD.encode(bytes),
            },
        )
        .await
    }
    /// Close the transport. The node closes the upstream session on disconnect.
    pub async fn close(&self) -> Result<()> {
        tokio::time::timeout(Duration::from_secs(3), async {
            self.sink.lock().await.close().await
        })
        .await
        .map_err(|_| Error::Protocol("realtime close deadline expired".into()))?
        .map_err(|e| Error::Protocol(e.to_string()))
    }
}

impl RealtimeEvents {
    /// Read the next event concurrently with writes. A generation mismatch is a protocol error.
    pub async fn next(&mut self) -> Option<Result<EventFrame>> {
        while let Some(message) = self.stream.next().await {
            match message {
                Ok(Message::Text(text)) => {
                    let event = serde_json::from_str::<EventFrame>(&text)
                        .map_err(|e| Error::Protocol(format!("invalid realtime event: {e}")));
                    return Some(event.and_then(|event| {
                        if event.session_id != self.session_id
                            || event.generation != self.generation
                        {
                            Err(Error::Protocol(
                                "realtime event belongs to a stale generation".into(),
                            ))
                        } else {
                            Ok(event)
                        }
                    }));
                }
                Ok(Message::Close(_)) => return None,
                Ok(Message::Ping(_) | Message::Pong(_)) => {}
                Ok(_) => {
                    return Some(Err(Error::Protocol(
                        "unexpected binary realtime frame".into(),
                    )));
                }
                Err(error) => return Some(Err(Error::Protocol(error.to_string()))),
            }
        }
        None
    }
}
