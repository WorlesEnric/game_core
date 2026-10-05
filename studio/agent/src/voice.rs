//! The voice bridge (04 §5): `WS /v1/voice` ↔ etos `GET /realtime/connect?provider=studio-voice`.
//!
//! Client → companion (JSON text frames):
//! - `{"type":"audio","seq":<n>,"pcm16":"<base64 PCM16 mono 24 kHz>"}` — at most 24 KiB raw
//!   (32 KiB base64) per frame (04 §2; a larger frame is refused with `too_large` and not
//!   forwarded); each frame becomes one etos `input_audio` command with the companion's own
//!   gapless sequence and ids `audio-<seq>`;
//! - `{"type":"stop"}` — close the session (pending transcripts are still forwarded).
//!
//! Companion → client:
//! - `{"type":"ready","sessionId","audioFormat":"pcm16","sampleRateHz":24000,"maxChunkBytes":24576}`;
//! - `{"type":"transcript","role":"user","itemId","revision","text","final"?}` — the user's
//!   speech only (other roles are dropped); a full revision, not a delta; `final: true` is
//!   present only on the last revision of an item (only that text belongs in the prompt box);
//! - `{"type":"speech_started","itemId"}`, `{"type":"speech_ended","itemId"}`;
//! - `{"type":"usage","usage"}`, `{"type":"error","code","message"}`, `{"type":"closed","reason"}`.
//!
//! Transcription only: the bridge never sends `response_create` (or any context or response
//! command); synthesized audio events are dropped. etos closes an idle session after 60 s
//! without input; the client sees `closed`.

use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use axum::extract::ws::{Message, WebSocket};
use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;
use etos_sdk::Client;
use etos_sdk::realtime::{Action, RealtimeConfig, RealtimeEvent};
use futures::{SinkExt, StreamExt};
use serde::Deserialize;
use serde_json::{Value, json};

use crate::config::VoiceConfig;
use crate::events::EventHub;
use crate::ledger::Ledger;
use crate::ops::{MediaOps, status};
use crate::util::new_id;

/// Largest raw PCM16 chunk sent to etos: 24 KiB, i.e. exactly 32 KiB of base64.
pub const MAX_CHUNK: usize = 24 * 1024;

#[derive(Debug, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum ClientMsg {
    Audio { seq: u64, pcm16: String },
    Stop,
}

/// The realtime refusal in an SDK error: `(code, message)`.
pub fn refusal_of(e: &etos_sdk::Error) -> (String, String) {
    match e {
        etos_sdk::Error::Refused { code, message, .. } => (code.clone(), message.clone()),
        etos_sdk::Error::Protocol(m) => {
            if let Some(i) = m.find('{')
                && let Ok(v) = serde_json::from_str::<Value>(&m[i..])
            {
                let err = v.get("error").unwrap_or(&v);
                if let Some(code) = err.get("code").and_then(Value::as_str) {
                    let message = err
                        .get("message")
                        .and_then(Value::as_str)
                        .unwrap_or_default();
                    return (code.to_string(), message.to_string());
                }
            }
            let code = if m.contains("403") {
                "forbidden"
            } else if m.contains("402") {
                "budget_exhausted"
            } else if m.contains("429") {
                "rate_limited"
            } else {
                "realtime_unavailable"
            };
            (code.to_string(), m.clone())
        }
        etos_sdk::Error::Transport(m) => ("transport".into(), m.clone()),
        etos_sdk::Error::Invalid(m) => ("invalid".into(), m.clone()),
    }
}

/// The node's refusal of a realtime upgrade, read again with a plain handshake: the SDK's
/// `open` reports only the HTTP status (`HTTP error: 503`) and drops the body
/// (`{code, message, hint}`). The probe is refused at admission like the first attempt (no
/// upstream session is opened; an unexpected success is closed before any configuration).
async fn probe_refusal(client: &Client, provider: &str) -> Option<(String, String)> {
    use tokio_tungstenite::tungstenite;
    use tokio_tungstenite::tungstenite::client::IntoClientRequest;
    let auth = etos_sdk::providers::url(client, provider).value;
    let url = format!(
        "{}/api/v1/realtime/connect?provider={provider}",
        client.url().replacen("http", "ws", 1)
    );
    let mut req = url.into_client_request().ok()?;
    req.headers_mut()
        .insert("authorization", auth.parse().ok()?);
    match tokio::time::timeout(
        Duration::from_secs(5),
        tokio_tungstenite::connect_async(req),
    )
    .await
    {
        Ok(Err(tungstenite::Error::Http(resp))) => {
            let body: Value = serde_json::from_slice(resp.body().as_deref()?).ok()?;
            let code = body.get("code")?.as_str()?.to_string();
            let message = body
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string();
            Some((code, message))
        }
        Ok(Ok((mut ws, _))) => {
            let _ = ws.close(None).await;
            None
        }
        _ => None,
    }
}

/// The voice bridge.
pub struct VoiceBridge {
    client: Client,
    cfg: VoiceConfig,
    ledger: Arc<Ledger>,
    hub: EventHub,
    ops: Arc<MediaOps>,
    active: AtomicUsize,
}

impl std::fmt::Debug for VoiceBridge {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("VoiceBridge")
            .field("provider", &self.cfg.provider)
            .finish_non_exhaustive()
    }
}

struct Active<'a>(&'a AtomicUsize);

impl Drop for Active<'_> {
    fn drop(&mut self) {
        self.0.fetch_sub(1, Ordering::SeqCst);
    }
}

type Sink = futures::stream::SplitSink<WebSocket, Message>;

async fn send(tx: &mut Sink, v: Value) -> bool {
    let v = crate::util::pruned(v);
    tx.send(Message::Text(v.to_string().into())).await.is_ok()
}

impl VoiceBridge {
    /// A bridge.
    pub fn new(
        client: Client,
        cfg: VoiceConfig,
        ledger: Arc<Ledger>,
        hub: EventHub,
        ops: Arc<MediaOps>,
    ) -> VoiceBridge {
        VoiceBridge {
            client,
            cfg,
            ledger,
            hub,
            ops,
            active: AtomicUsize::new(0),
        }
    }

    /// Sessions open now.
    pub fn active(&self) -> usize {
        self.active.load(Ordering::SeqCst)
    }

    /// Serve one upgraded `WS /v1/voice` connection for `app`.
    pub async fn run(self: Arc<Self>, socket: WebSocket, app: String) {
        let (mut tx, mut rx) = socket.split();
        if self.active.fetch_add(1, Ordering::SeqCst) >= self.cfg.max_sessions {
            self.active.fetch_sub(1, Ordering::SeqCst);
            send(
                &mut tx,
                json!({"type": "error", "code": "rate_limited",
                "message": "a voice session is already open for this companion"}),
            )
            .await;
            send(&mut tx, json!({"type": "closed", "reason": "busy"})).await;
            let _ = tx.close().await;
            return;
        }
        let _active = Active(&self.active);
        let session_id = new_id("vs");
        let config = RealtimeConfig {
            session_id: session_id.clone(),
            generation: 1,
            instructions: self.cfg.instructions.clone(),
            voice: None,
            audio_format: "pcm16".into(),
            sample_rate_hz: 24_000,
            required: Default::default(),
        };
        let opened = tokio::time::timeout(
            Duration::from_secs(25),
            self.client.realtime().open(&self.cfg.provider, &config),
        )
        .await;
        let (sender, mut events) = match opened {
            Ok(Ok(pair)) => pair,
            Ok(Err(e)) => {
                let (mut code, mut message) = refusal_of(&e);
                if message.contains("HTTP error")
                    && let Some((c, m)) = probe_refusal(&self.client, &self.cfg.provider).await
                {
                    code = c;
                    message = m;
                }
                match code.as_str() {
                    "not_configured" => self.ops.set_voice_status(status::NOT_CONFIGURED),
                    "not_granted" | "forbidden" | "budget_exhausted" => {
                        self.ops.set_voice_status(status::BLOCKED)
                    }
                    _ => {}
                }
                tracing::warn!(provider = %self.cfg.provider, code = %code, "voice session refused");
                send(
                    &mut tx,
                    json!({"type": "error", "code": code, "message": message}),
                )
                .await;
                send(&mut tx, json!({"type": "closed", "reason": "refused"})).await;
                let _ = tx.close().await;
                return;
            }
            Err(_) => {
                send(
                    &mut tx,
                    json!({"type": "error", "code": "deadline",
                    "message": "the realtime session did not open within 25 s"}),
                )
                .await;
                send(&mut tx, json!({"type": "closed", "reason": "deadline"})).await;
                let _ = tx.close().await;
                return;
            }
        };
        self.ops.set_voice_status(status::LIVE);
        if let Err(e) = self
            .ledger
            .voice_started(&session_id, &app, &self.cfg.provider)
        {
            tracing::error!(error = %e, "cannot record the voice session");
        }
        let _ = self.hub.emit(
            "voice_session",
            None,
            &json!({"sessionId": session_id, "state": "started", "provider": self.cfg.provider}),
        );
        tracing::info!(session = %session_id, "voice session open");
        send(
            &mut tx,
            json!({"type": "ready", "sessionId": session_id, "audioFormat": "pcm16",
            "sampleRateHz": 24_000, "maxChunkBytes": MAX_CHUNK}),
        )
        .await;

        let mut seq: u64 = 0;
        let mut chunks: u64 = 0;
        let mut audio_bytes: u64 = 0;
        let mut transcripts: u64 = 0;
        let mut closed_sent = false;
        let mut draining: Option<tokio::time::Instant> = None;
        let reason: String;
        loop {
            let drain_deadline = async {
                match draining {
                    Some(d) => tokio::time::sleep_until(d).await,
                    None => futures::future::pending().await,
                }
            };
            tokio::select! {
                msg = rx.next(), if draining.is_none() => {
                    match msg {
                        Some(Ok(Message::Text(text))) => match serde_json::from_str::<ClientMsg>(text.as_str()) {
                            Ok(ClientMsg::Audio { seq: cseq, pcm16 }) => {
                                if cseq != seq {
                                    send(&mut tx, json!({"type": "error", "code": "audio_gap",
                                        "message": format!("expected chunk {seq}, got {cseq}")})).await;
                                    continue;
                                }
                                let bytes = match STANDARD.decode(pcm16.as_bytes()) {
                                    Ok(b) if b.len() > MAX_CHUNK => {
                                        send(&mut tx, json!({"type": "error", "code": "too_large",
                                            "message": format!("an audio frame is at most {MAX_CHUNK} bytes of PCM16 ({} base64); this one has {}", MAX_CHUNK / 3 * 4, b.len())})).await;
                                        continue;
                                    }
                                    Ok(b) if !b.is_empty() && b.len().is_multiple_of(2) => b,
                                    _ => {
                                        send(&mut tx, json!({"type": "error", "code": "bad_audio",
                                            "message": "pcm16 is base64 of a non-empty, even number of bytes"})).await;
                                        continue;
                                    }
                                };
                                let mut failed = None;
                                for chunk in bytes.chunks(MAX_CHUNK) {
                                    if let Err(e) = sender.audio(seq, chunk).await {
                                        failed = Some(e);
                                        break;
                                    }
                                    seq += 1;
                                    chunks += 1;
                                }
                                audio_bytes += bytes.len() as u64;
                                if let Some(e) = failed {
                                    let (code, message) = refusal_of(&e);
                                    send(&mut tx, json!({"type": "error", "code": code, "message": message})).await;
                                    reason = "audio send failed".into();
                                    break;
                                }
                            }
                            Ok(ClientMsg::Stop) => {
                                // Ask etos to close; keep forwarding until it does (≤ 3 s).
                                let _ = sender.command("close-1", Action::Close).await;
                                draining = Some(tokio::time::Instant::now() + Duration::from_secs(3));
                            }
                            Err(_) => {
                                send(&mut tx, json!({"type": "error", "code": "bad_message",
                                    "message": "expected {\"type\":\"audio\",\"seq\",\"pcm16\"} or {\"type\":\"stop\"}"})).await;
                            }
                        },
                        Some(Ok(Message::Binary(_))) => {
                            send(&mut tx, json!({"type": "error", "code": "bad_message",
                                "message": "send JSON text frames"})).await;
                        }
                        Some(Ok(Message::Ping(_) | Message::Pong(_))) => {}
                        Some(Ok(Message::Close(_))) | None => {
                            reason = "client closed".into();
                            break;
                        }
                        Some(Err(_)) => {
                            reason = "client connection failed".into();
                            break;
                        }
                    }
                }
                ev = events.next() => {
                    match ev {
                        Some(Ok(frame)) => match frame.event {
                            RealtimeEvent::Transcript { role, item_id, revision, text, done, .. } => {
                                // The user's speech only (04 §2).
                                if role != "user" {
                                    continue;
                                }
                                let mut frame = json!({"type": "transcript", "role": "user", "itemId": item_id,
                                    "revision": revision, "text": text});
                                if done {
                                    transcripts += 1;
                                    frame["final"] = json!(true);
                                    let _ = self.hub.emit("voice_transcript", None,
                                        &json!({"sessionId": session_id, "itemId": item_id, "text": text}));
                                }
                                send(&mut tx, frame).await;
                            }
                            RealtimeEvent::SpeechStarted { item_id } => {
                                send(&mut tx, json!({"type": "speech_started", "itemId": item_id})).await;
                            }
                            RealtimeEvent::SpeechEnded { item_id } => {
                                send(&mut tx, json!({"type": "speech_ended", "itemId": item_id})).await;
                            }
                            RealtimeEvent::Usage { usage } => {
                                send(&mut tx, json!({"type": "usage", "usage": usage})).await;
                            }
                            RealtimeEvent::Error { code, message } => {
                                send(&mut tx, json!({"type": "error", "code": code, "message": message})).await;
                            }
                            RealtimeEvent::Closed { reason: r } => {
                                send(&mut tx, json!({"type": "closed", "reason": r})).await;
                                closed_sent = true;
                                reason = r;
                                break;
                            }
                            // Never asked for: synthesized audio and responses are dropped.
                            RealtimeEvent::Audio { .. }
                            | RealtimeEvent::ResponseEnded { .. }
                            | RealtimeEvent::Ready { .. }
                            | RealtimeEvent::ControlApplied { .. } => {}
                        },
                        Some(Err(e)) => {
                            let (code, message) = refusal_of(&e);
                            send(&mut tx, json!({"type": "error", "code": code, "message": message})).await;
                            reason = "realtime protocol error".into();
                            break;
                        }
                        None => {
                            reason = if draining.is_some() { "stopped".into() } else { "etos closed the session".into() };
                            break;
                        }
                    }
                }
                _ = drain_deadline => {
                    reason = "stopped".into();
                    break;
                }
            }
        }
        let _ = sender.close().await;
        if !closed_sent {
            send(&mut tx, json!({"type": "closed", "reason": reason})).await;
        }
        let _ = tx.close().await;
        if let Err(e) =
            self.ledger
                .voice_ended(&session_id, &reason, chunks, audio_bytes, transcripts)
        {
            tracing::error!(error = %e, "cannot record the voice session end");
        }
        let _ = self.hub.emit(
            "voice_session",
            None,
            &json!({"sessionId": session_id, "state": "ended", "reason": reason,
                    "chunks": chunks, "transcripts": transcripts}),
        );
        tracing::info!(session = %session_id, reason = %reason, chunks, transcripts, "voice session closed");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn realtime_refusals_are_recovered_from_sdk_errors() {
        let e = etos_sdk::Error::Protocol(
            r#"realtime session refused: {"error":{"code":"not_configured","message":"no adapter"}}"#.into(),
        );
        assert_eq!(refusal_of(&e).0, "not_configured");
        let e = etos_sdk::Error::Protocol(
            "realtime connection failed: HTTP error: 403 Forbidden".into(),
        );
        assert_eq!(refusal_of(&e).0, "forbidden");
        assert_eq!(MAX_CHUNK * 4 / 3, 32 * 1024);
        let m: ClientMsg =
            serde_json::from_str(r#"{"type":"audio","seq":3,"pcm16":"AAA="}"#).unwrap();
        assert!(matches!(m, ClientMsg::Audio { seq: 3, .. }));
        assert!(matches!(
            serde_json::from_str::<ClientMsg>(r#"{"type":"stop"}"#).unwrap(),
            ClientMsg::Stop
        ));
    }
}
