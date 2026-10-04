//! The agent channel's connection loop: connect, say hello and wait for the node's welcome
//! (or its refusal), re-register and report ready after a reconnect, dispatch calls, answer pings, and reconnect with backoff when the
//! connection drops. Calls still pending on the node are delivered again after a reconnect;
//! a call already running keeps running, and a finished one is answered from the result
//! cache, so the same process never runs a call twice.

use std::sync::Arc;
use std::sync::atomic::Ordering;
use std::time::Duration;

use futures::{SinkExt, StreamExt};
use serde::Deserialize;
use serde_json::{Value, json};
use tokio::net::TcpStream;
use tokio::sync::{mpsc, watch};
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::http::HeaderValue;
use tokio_tungstenite::tungstenite::http::header::AUTHORIZATION;
use tokio_tungstenite::tungstenite::{self, Message};
use tokio_tungstenite::{MaybeTlsStream, WebSocketStream};

use super::{Call, CallContext, CallKind, Handler, Inner, Reply, SDK, Status, Welcome};
use crate::client::refusal_error;
use crate::error::{Error, Refusal, Result};
use crate::util::jitter;

type Ws = WebSocketStream<MaybeTlsStream<TcpStream>>;

/// A message from the node.
#[derive(Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum Incoming {
    Call(Box<CallMsg>),
    Cancel {
        id: String,
    },
    Shutdown,
    Ping,
    /// A message type this SDK does not know (newer nodes may add some).
    #[serde(other)]
    Unknown,
}

#[derive(Deserialize)]
struct CallMsg {
    id: String,
    kind: CallKind,
    name: String,
    #[serde(default)]
    args: Value,
    #[serde(default)]
    context: CallContext,
    #[serde(default)]
    deadline_ms: Option<u64>,
}

/// How a connection ended.
enum End {
    /// The node asked the agent to shut down.
    Shutdown,
    /// Closed locally.
    Closed,
    /// The connection dropped; reconnect.
    Dropped(String),
}

/// How long the node has to answer the hello.
const WELCOME_WITHIN: Duration = Duration::from_secs(10);

/// The node's first answer to the hello.
#[derive(Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum Answer {
    Welcome(Welcome),
    Refused { error: Refusal },
}

/// Connect, say hello and wait for the welcome.
pub(super) async fn open(inner: &Inner) -> Result<Ws> {
    let base = inner.client.url();
    let url = if let Some(rest) = base.strip_prefix("https://") {
        format!("wss://{rest}/api/v1/agent/connect")
    } else if let Some(rest) = base.strip_prefix("http://") {
        format!("ws://{rest}/api/v1/agent/connect")
    } else {
        return Err(Error::Invalid(format!(
            "the node URL {base:?} is not http(s)"
        )));
    };
    let mut req = url
        .into_client_request()
        .map_err(|e| Error::Invalid(format!("bad agent channel URL: {e}")))?;
    let auth = HeaderValue::from_str(&inner.client.bearer())
        .map_err(|_| Error::Invalid("the key is not a valid header value".into()))?;
    req.headers_mut().insert(AUTHORIZATION, auth);
    let connect = tokio_tungstenite::connect_async(req);
    let (mut ws, _) = match tokio::time::timeout(Duration::from_secs(10), connect).await {
        Err(_) => {
            return Err(Error::Transport(
                "the agent channel did not open within 10 s".into(),
            ));
        }
        Ok(Err(e)) => return Err(ws_error(e)),
        Ok(Ok(pair)) => pair,
    };
    let hello = json!({"type": "hello", "sdk": SDK, "agent": inner.name});
    ws.send(Message::Text(hello.to_string().into()))
        .await
        .map_err(ws_error)?;
    let welcome = match tokio::time::timeout(WELCOME_WITHIN, answer(&mut ws)).await {
        Ok(w) => w?,
        Err(_) => {
            return Err(Error::Transport(format!(
                "the node did not answer the hello within {} s",
                WELCOME_WITHIN.as_secs()
            )));
        }
    };
    if let Ok(mut w) = inner.welcome.write() {
        *w = Some(welcome);
    }
    Ok(ws)
}

/// The node's answer to the hello: its welcome, or its refusal (then it closes).
async fn answer(ws: &mut Ws) -> Result<Welcome> {
    loop {
        let msg = match ws.next().await {
            Some(Ok(m)) => m,
            Some(Err(e)) => return Err(ws_error(e)),
            None => return Err(Error::Transport("the node closed the agent channel".into())),
        };
        let text = match msg {
            Message::Text(t) => t,
            Message::Close(frame) => {
                let why = frame.map(|f| f.reason.to_string()).unwrap_or_default();
                return Err(Error::Transport(format!(
                    "the node closed the agent channel: {why}"
                )));
            }
            _ => continue,
        };
        return match serde_json::from_str::<Answer>(text.as_str()) {
            Ok(Answer::Welcome(w)) => Ok(w),
            Ok(Answer::Refused { error }) => Err(Error::Refused {
                status: 403,
                code: error.code,
                message: error.message,
                hint: error.hint,
            }),
            Err(e) => Err(Error::Protocol(format!(
                "the node answered the hello with an unexpected message: {e}"
            ))),
        };
    }
}

fn ws_error(e: tungstenite::Error) -> Error {
    match e {
        tungstenite::Error::Http(resp) => {
            let body: Vec<u8> = resp.body().as_ref().map(|b| b.to_vec()).unwrap_or_default();
            refusal_error(resp.status().as_u16(), &body)
        }
        other => Error::Transport(format!("agent channel: {other}")),
    }
}

/// The node refused the agent itself; reconnecting cannot help.
fn fatal(e: &Error) -> bool {
    matches!(e, Error::Refused { status, .. } if (400..500).contains(status) && *status != 408 && *status != 429)
        || matches!(e, Error::Invalid(_))
}

/// Serve connections until shutdown, local close, or a fatal refusal.
pub(super) async fn run(inner: Arc<Inner>, first: Ws) {
    let mut next = Some(first);
    let mut reconnected = false;
    let mut delay = inner.options.reconnect_min;
    loop {
        if inner.stopping() {
            return;
        }
        let ws = match next.take() {
            Some(ws) => Ok(ws),
            None => open(&inner).await,
        };
        match ws {
            Ok(ws) => {
                delay = inner.options.reconnect_min;
                match session(&inner, ws, reconnected).await {
                    End::Shutdown => {
                        let _ = inner.status.send_replace(Status::Shutdown);
                        cancel_all(&inner);
                        return;
                    }
                    End::Closed => return,
                    End::Dropped(why) => {
                        tracing::warn!(agent = %inner.name, reason = %why, "etos agent channel dropped; reconnecting");
                    }
                }
                reconnected = true;
            }
            Err(e) if fatal(&e) => {
                tracing::error!(agent = %inner.name, error = %e, "etos agent channel refused");
                let _ = inner.status.send_replace(Status::Failed(e));
                cancel_all(&inner);
                return;
            }
            Err(e) => {
                tracing::warn!(agent = %inner.name, error = %e, "etos agent channel: cannot connect");
            }
        }
        let mut status = inner.status.subscribe();
        tokio::select! {
            _ = tokio::time::sleep(jitter(delay)) => {}
            _ = stopped(&mut status) => return,
        }
        delay = (delay * 2).min(inner.options.reconnect_max);
    }
}

async fn session(inner: &Arc<Inner>, ws: Ws, reconnected: bool) -> End {
    let (mut sink, mut stream) = ws.split();
    let (tx, mut rx) = mpsc::unbounded_channel::<String>();
    if let Ok(mut o) = inner.outbox.lock() {
        *o = Some(tx.clone());
    }
    let _ = inner.connected.send_replace(true);
    let end = drive(inner, &mut sink, &mut stream, &tx, &mut rx, reconnected).await;
    if let Ok(mut o) = inner.outbox.lock() {
        *o = None;
    }
    let _ = inner.connected.send_replace(false);
    if matches!(end, End::Closed | End::Shutdown) {
        let _ = sink.send(Message::Close(None)).await;
    }
    end
}

async fn drive(
    inner: &Arc<Inner>,
    sink: &mut futures::stream::SplitSink<Ws, Message>,
    stream: &mut futures::stream::SplitStream<Ws>,
    tx: &mpsc::UnboundedSender<String>,
    rx: &mut mpsc::UnboundedReceiver<String>,
    reconnected: bool,
) -> End {
    if reconnected {
        // The node may have restarted and forgotten registrations: repeat them (idempotent).
        if let Err(e) = reregister(inner).await {
            return End::Dropped(format!("re-registration failed: {e}"));
        }
    }
    if inner.ready.load(Ordering::SeqCst) {
        let _ = tx.send(json!({"type": "ready"}).to_string());
    }
    let mut status = inner.status.subscribe();
    loop {
        tokio::select! {
            out = rx.recv() => {
                let Some(text) = out else { return End::Dropped("outbox closed".into()) };
                if let Err(e) = sink.send(Message::Text(text.into())).await {
                    return End::Dropped(format!("send failed: {e}"));
                }
            }
            msg = tokio::time::timeout(inner.options.idle_timeout, stream.next()) => {
                let msg = match msg {
                    Err(_) => return End::Dropped(format!(
                        "no message from the node for {} s", inner.options.idle_timeout.as_secs()
                    )),
                    Ok(None) => return End::Dropped("the node closed the connection".into()),
                    Ok(Some(Err(e))) => return End::Dropped(e.to_string()),
                    Ok(Some(Ok(m))) => m,
                };
                match msg {
                    Message::Text(text) => {
                        if handle(inner, tx, text.as_str()) {
                            return End::Shutdown;
                        }
                    }
                    Message::Close(_) => return End::Dropped("the node closed the connection".into()),
                    _ => {}
                }
            }
            _ = stopped(&mut status) => return End::Closed,
        }
    }
}

/// Resolves when the agent stops running (shut down, closed or refused). The watch guard is
/// dropped inside, so the future is `Send`.
async fn stopped(status: &mut watch::Receiver<Status>) {
    let _ = status.wait_for(|s| *s != Status::Running).await;
}

async fn reregister(inner: &Arc<Inner>) -> Result<()> {
    let tools: Vec<(String, super::ToolSpec)> = match inner.tools.read() {
        Ok(t) => t.iter().map(|(k, (s, _))| (k.clone(), s.clone())).collect(),
        Err(_) => return Err(Error::Invalid("the tool table is unusable".into())),
    };
    for (name, spec) in &tools {
        super::register_tool(inner, name, spec).await?;
    }
    let any_service = inner
        .services
        .read()
        .map(|s| !s.is_empty())
        .unwrap_or(false);
    if any_service {
        super::register_services(inner).await?;
    }
    let endpoint = inner
        .endpoint_url
        .read()
        .map_err(|_| Error::Invalid("the endpoint registration is unusable".into()))?
        .clone();
    if let Some(url) = endpoint {
        super::Agent {
            inner: inner.clone(),
        }
        .endpoint(&url)
        .await?;
    }
    Ok(())
}

/// Handle one text message; true when the node asked the agent to shut down.
fn handle(inner: &Arc<Inner>, tx: &mpsc::UnboundedSender<String>, text: &str) -> bool {
    let msg: Incoming = match serde_json::from_str(text) {
        Ok(m) => m,
        Err(e) => {
            tracing::warn!(agent = %inner.name, error = %e, "etos agent channel: malformed message ignored");
            return false;
        }
    };
    match msg {
        Incoming::Call(call) => dispatch(inner, tx, *call),
        Incoming::Cancel { id } => {
            if let Some(c) = inner.running.lock().ok().and_then(|r| r.get(&id).cloned()) {
                let _ = c.send_replace(true);
            }
        }
        Incoming::Ping => {
            let _ = tx.send(json!({"type": "pong"}).to_string());
        }
        Incoming::Shutdown => return true,
        Incoming::Unknown => {}
    }
    false
}

fn dispatch(inner: &Arc<Inner>, tx: &mpsc::UnboundedSender<String>, msg: CallMsg) {
    let id = msg.id.clone();
    // Redelivered while still running: the running call answers when it finishes.
    if inner
        .running
        .lock()
        .map(|r| r.contains_key(&id))
        .unwrap_or(false)
    {
        return;
    }
    // Redelivered after it finished: answer from the cache.
    if let Some(cached) = inner.cache.lock().ok().and_then(|mut c| c.get(&id)) {
        let _ = tx.send(cached);
        return;
    }
    let short = msg.name;
    let handler: Option<Handler> = match msg.kind {
        CallKind::Tool => inner
            .tools
            .read()
            .ok()
            .and_then(|t| t.get(&short).map(|(_, h)| h.clone())),
        CallKind::Service => inner
            .services
            .read()
            .ok()
            .and_then(|s| s.get(&short).map(|(_, h)| h.clone())),
    };
    let Some(handler) = handler else {
        let (code, what) = match msg.kind {
            CallKind::Tool => ("unknown_tool", "tool"),
            CallKind::Service => ("unknown_method", "service method"),
        };
        let refusal = Refusal::new(
            code,
            format!("agent {} has no {what} {short:?}", inner.name),
        );
        let _ = tx.send(result_message(&id, Err(refusal)));
        return;
    };
    let (cancel_tx, cancel_rx) = watch::channel(false);
    let cancel_tx = Arc::new(cancel_tx);
    if let Ok(mut r) = inner.running.lock() {
        r.insert(id.clone(), cancel_tx.clone());
    }
    let deadline = msg
        .deadline_ms
        .map(|ms| tokio::time::Instant::now() + Duration::from_millis(ms));
    let call = Call {
        id: id.clone(),
        kind: msg.kind,
        name: short,
        args: msg.args,
        context: msg.context,
        deadline,
        cancel: cancel_rx,
        inner: inner.clone(),
    };
    let inner = inner.clone();
    tokio::spawn(async move {
        let work = handler(call);
        tokio::pin!(work);
        let mut deadline = deadline;
        let reply = loop {
            tokio::select! {
                r = &mut work => break r,
                _ = sleep_until(deadline), if deadline.is_some() => {
                    let _ = cancel_tx.send_replace(true);
                    deadline = None;
                }
            }
        };
        let message = result_message(&id, reply);
        if let Ok(mut c) = inner.cache.lock() {
            c.insert(&id, message.clone());
        }
        if let Ok(mut r) = inner.running.lock() {
            r.remove(&id);
        }
        // Lost when the channel is down; the node delivers the call again and the cache answers.
        inner.send(message);
    });
}

async fn sleep_until(deadline: Option<tokio::time::Instant>) {
    match deadline {
        Some(d) => tokio::time::sleep_until(d).await,
        None => futures::future::pending().await,
    }
}

/// The `result` message of a call.
fn result_message(id: &str, reply: std::result::Result<Reply, Refusal>) -> String {
    let msg = match reply {
        Ok(r) => {
            let mut m = json!({"type": "result", "id": id, "ok": true});
            if let Some(text) = r.text {
                m["text"] = Value::String(text);
            }
            if !r.refs.is_empty() {
                m["refs"] = json!(r.refs);
            }
            if let Some(data) = r.data {
                m["data"] = data;
            }
            m
        }
        Err(refusal) => json!({"type": "result", "id": id, "ok": false, "error": refusal}),
    };
    msg.to_string()
}

/// Cancel every call in flight.
pub(super) fn cancel_all(inner: &Inner) {
    if let Ok(r) = inner.running.lock() {
        for c in r.values() {
            let _ = c.send_replace(true);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn result_messages_follow_the_wire() {
        let ok: Value = serde_json::from_str(&result_message(
            "c1",
            Ok(Reply::text("hi").with_refs(vec!["ref_1".into()])),
        ))
        .unwrap();
        assert_eq!(
            ok,
            json!({"type": "result", "id": "c1", "ok": true, "text": "hi", "refs": ["ref_1"]})
        );
        let err: Value = serde_json::from_str(&result_message(
            "c2",
            Err(Refusal::new("busy", "try later").with_hint("wait a minute")),
        ))
        .unwrap();
        assert_eq!(
            err,
            json!({"type": "result", "id": "c2", "ok": false,
                   "error": {"code": "busy", "message": "try later", "hint": "wait a minute"}})
        );
    }

    #[test]
    fn incoming_messages_parse_and_unknown_types_are_tolerated() {
        let m: Incoming = serde_json::from_str(
            r#"{"type":"call","id":"c1","kind":"tool","name":"echo.say","args":{"x":1},"context":{"task":"t1"},"deadline_ms":500}"#,
        )
        .unwrap();
        let Incoming::Call(c) = m else {
            panic!("not a call")
        };
        assert_eq!(
            (c.id.as_str(), c.kind, c.deadline_ms),
            ("c1", CallKind::Tool, Some(500))
        );
        assert_eq!(c.context.task.as_deref(), Some("t1"));
        assert!(matches!(
            serde_json::from_str(r#"{"type":"welcome","token":"x"}"#),
            Ok(Incoming::Unknown)
        ));
        assert!(matches!(
            serde_json::from_str(r#"{"type":"cancel","id":"c1"}"#),
            Ok(Incoming::Cancel { .. })
        ));
        assert!(fatal(&Error::Refused {
            status: 401,
            code: "unauthorized".into(),
            message: String::new(),
            hint: None
        }));
        assert!(!fatal(&Error::Refused {
            status: 503,
            code: "x".into(),
            message: String::new(),
            hint: None
        }));
    }
}
