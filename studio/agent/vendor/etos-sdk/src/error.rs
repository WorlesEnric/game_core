//! Errors of the SDK, and the refusal a handler answers with.

use serde::{Deserialize, Serialize};

/// Result of an SDK call.
pub type Result<T> = std::result::Result<T, Error>;

/// Why an SDK call failed.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum Error {
    /// The node refused the request. `code`, `message` and `hint` are the node's refusal.
    #[error("{message} ({code}, HTTP {status})")]
    Refused {
        /// HTTP status of the answer.
        status: u16,
        /// Stable machine-readable code.
        code: String,
        /// What happened, for the developer.
        message: String,
        /// What to do instead, when there is something to do.
        hint: Option<String>,
    },
    /// The node could not be reached, the connection failed, or the request timed out.
    #[error("transport: {0}")]
    Transport(String),
    /// The node answered with something the SDK does not understand (a body that is not the
    /// expected JSON, a malformed channel message).
    #[error("protocol: {0}")]
    Protocol(String),
    /// Refused locally, before anything was sent: bad configuration or arguments.
    #[error("invalid: {0}")]
    Invalid(String),
}

impl Error {
    /// True when repeating the same request later can succeed: a transport failure, or a
    /// refusal with status 408, 429 or 5xx.
    pub fn is_retryable(&self) -> bool {
        match self {
            Error::Transport(_) => true,
            Error::Refused { status, .. } => *status == 408 || *status == 429 || *status >= 500,
            Error::Protocol(_) | Error::Invalid(_) => false,
        }
    }

    /// The refusal code: the node's code, or `transport`, `protocol`, `invalid`.
    pub fn code(&self) -> &str {
        match self {
            Error::Refused { code, .. } => code,
            Error::Transport(_) => "transport",
            Error::Protocol(_) => "protocol",
            Error::Invalid(_) => "invalid",
        }
    }
}

/// A refusal: the serialisable `{code, message, hint}` every etos wire uses. Tool and service
/// handlers answer with one when they cannot do what was asked.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Refusal {
    /// Stable machine-readable code.
    pub code: String,
    /// What happened, written for whoever reads it (a model, an app developer).
    pub message: String,
    /// What to do instead.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hint: Option<String>,
}

impl Refusal {
    /// A refusal with a code and a message.
    pub fn new(code: impl Into<String>, message: impl Into<String>) -> Refusal {
        Refusal {
            code: code.into(),
            message: message.into(),
            hint: None,
        }
    }

    /// The same refusal with a hint.
    pub fn with_hint(mut self, hint: impl Into<String>) -> Refusal {
        self.hint = Some(hint.into());
        self
    }
}

impl std::fmt::Display for Refusal {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{} ({})", self.message, self.code)
    }
}

impl std::error::Error for Refusal {}

impl From<Error> for Refusal {
    /// A failed SDK call inside a handler becomes the handler's refusal: a node refusal keeps
    /// its code, message and hint.
    fn from(e: Error) -> Refusal {
        match e {
            Error::Refused {
                code,
                message,
                hint,
                ..
            } => Refusal {
                code,
                message,
                hint,
            },
            other => Refusal::new(other.code().to_string(), other.to_string()),
        }
    }
}
