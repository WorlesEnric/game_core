//! Etos-shaped errors of the Unity-facing API: `{code, message, hint}` with an HTTP status.
//!
//! etos refusals pass through unchanged (status, code, message, hint). The companion adds
//! only [`CANDIDATE_INVALID`], [`STALE_CONTEXT`], [`STAGE_FAILED`] and [`LEDGER_CONFLICT`],
//! plus the generic request codes every etos route uses (`bad_request`, `not_found`,
//! `forbidden`, `agent_starting`, `too_large`, `internal`).

use axum::Json;
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use serde::{Deserialize, Serialize};

use crate::redact::redact;

/// The worker's change set failed digest, manifest or schema checks.
pub const CANDIDATE_INVALID: &str = "candidate_invalid";
/// The request references context the companion does not have (an unknown tool catalog
/// revision) or that is outdated.
pub const STALE_CONTEXT: &str = "stale_context";
/// The staging command is missing or failed to produce a verdict.
pub const STAGE_FAILED: &str = "stage_failed";
/// The ledger already holds a different request under the same id.
pub const LEDGER_CONFLICT: &str = "ledger_conflict";

/// The serialised body of an error.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ErrorBody {
    /// Stable machine-readable code.
    pub code: String,
    /// What happened.
    pub message: String,
    /// What to do instead, when there is something to do.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hint: Option<String>,
}

/// An error answered to Unity.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ApiError {
    /// HTTP status.
    pub status: StatusCode,
    /// Code, message and hint.
    pub body: ErrorBody,
}

/// Result of an API operation.
pub type ApiResult<T> = Result<T, ApiError>;

impl ApiError {
    /// An error with a status, a code and a message.
    pub fn new(status: StatusCode, code: &str, message: impl Into<String>) -> ApiError {
        ApiError {
            status,
            body: ErrorBody {
                code: code.to_string(),
                message: redact(&message.into()),
                hint: None,
            },
        }
    }

    /// The same error with a hint.
    pub fn with_hint(mut self, hint: impl Into<String>) -> ApiError {
        self.body.hint = Some(redact(&hint.into()));
        self
    }

    /// 400 `bad_request`.
    pub fn bad_request(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::BAD_REQUEST, "bad_request", message)
    }

    /// 404 `not_found`.
    pub fn not_found(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::NOT_FOUND, "not_found", message)
    }

    /// 403 `forbidden`.
    pub fn forbidden(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::FORBIDDEN, "forbidden", message)
    }

    /// 500 `internal`.
    pub fn internal(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::INTERNAL_SERVER_ERROR, "internal", message)
    }

    /// 409 [`LEDGER_CONFLICT`].
    pub fn ledger_conflict(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::CONFLICT, LEDGER_CONFLICT, message)
    }

    /// 409 [`STALE_CONTEXT`].
    pub fn stale_context(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::CONFLICT, STALE_CONTEXT, message)
    }

    /// 503 [`STAGE_FAILED`].
    pub fn stage_failed(message: impl Into<String>) -> ApiError {
        ApiError::new(StatusCode::SERVICE_UNAVAILABLE, STAGE_FAILED, message)
    }

    /// The code.
    pub fn code(&self) -> &str {
        &self.body.code
    }
}

impl std::fmt::Display for ApiError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "{} ({}, HTTP {})",
            self.body.message,
            self.body.code,
            self.status.as_u16()
        )
    }
}

impl std::error::Error for ApiError {}

impl From<etos_sdk::Error> for ApiError {
    /// A node refusal keeps its status, code, message and hint; anything else is a gateway
    /// failure (`transport`, `protocol`) or a local fault (`invalid`).
    fn from(e: etos_sdk::Error) -> ApiError {
        match e {
            etos_sdk::Error::Refused {
                status,
                code,
                message,
                hint,
            } => {
                let status = StatusCode::from_u16(status).unwrap_or(StatusCode::BAD_GATEWAY);
                let mut err = ApiError::new(status, &code, message);
                if let Some(h) = hint {
                    err = err.with_hint(h);
                }
                err
            }
            etos_sdk::Error::Transport(m) => ApiError::new(StatusCode::BAD_GATEWAY, "transport", m)
                .with_hint("the etos node is unreachable from the companion; it retries"),
            etos_sdk::Error::Protocol(m) => ApiError::new(StatusCode::BAD_GATEWAY, "protocol", m),
            etos_sdk::Error::Invalid(m) => {
                ApiError::new(StatusCode::INTERNAL_SERVER_ERROR, "invalid", m)
            }
        }
    }
}

impl From<crate::ledger::LedgerError> for ApiError {
    fn from(e: crate::ledger::LedgerError) -> ApiError {
        ApiError::internal(format!("ledger: {e}"))
    }
}

impl IntoResponse for ApiError {
    fn into_response(self) -> Response {
        (self.status, Json(self.body)).into_response()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn refusals_pass_through() {
        let e: ApiError = etos_sdk::Error::Refused {
            status: 404,
            code: "not_configured".into(),
            message: "image generation is not available".into(),
            hint: Some("add a provider".into()),
        }
        .into();
        assert_eq!(e.status, StatusCode::NOT_FOUND);
        assert_eq!(e.code(), "not_configured");
        assert_eq!(e.body.hint.as_deref(), Some("add a provider"));
        let e: ApiError = etos_sdk::Error::Transport("down".into()).into();
        assert_eq!(e.status, StatusCode::BAD_GATEWAY);
        assert_eq!(e.code(), "transport");
    }

    #[test]
    fn messages_are_redacted() {
        let e = ApiError::bad_request("key etk_abcdefghijklmnop leaked");
        assert!(!e.body.message.contains("etk_abc"));
    }
}
