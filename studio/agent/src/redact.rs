//! Redaction of credentials in anything the companion writes: log lines and error messages.
//!
//! etos keys and tokens carry recognisable prefixes (`etk_` app/agent keys, `ett_` tickets,
//! `etp_` proxy tokens); `Bearer <value>` headers are redacted whatever the value. The log
//! writer ([`RedactingStderr`]) applies [`redact`] to every formatted line, so a token that
//! slips into an error message from a library is still not written. The companion never logs
//! its environment (it inherits etosd's, provider keys included).

use std::io::Write;

/// Prefixes of etos credentials.
const PREFIXES: &[&str] = &["etk_", "ett_", "etp_", "eta_", "sk-"];

fn token_char(c: char) -> bool {
    c.is_ascii_alphanumeric() || matches!(c, '_' | '-' | '.' | '~' | '+' | '/' | '=')
}

/// `text` with every etos credential and bearer value replaced by `[redacted]`.
pub fn redact(text: &str) -> String {
    let cleaned = redact_json_fields(text);
    let text = cleaned.as_str();
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    'outer: while !rest.is_empty() {
        // Bearer values (case-insensitive scheme).
        if rest.len() >= 7 && rest.is_char_boundary(7) && rest[..7].eq_ignore_ascii_case("bearer ")
        {
            out.push_str(&rest[..7]);
            let after = &rest[7..];
            let end = after
                .char_indices()
                .find(|(_, c)| !token_char(*c))
                .map(|(i, _)| i)
                .unwrap_or(after.len());
            if end > 0 {
                out.push_str("[redacted]");
            }
            rest = &after[end..];
            continue;
        }
        for p in PREFIXES {
            if rest.starts_with(p) {
                let after = &rest[p.len()..];
                let end = after
                    .char_indices()
                    .find(|(_, c)| !token_char(*c))
                    .map(|(i, _)| i)
                    .unwrap_or(after.len());
                if end >= 4 {
                    out.push_str(p);
                    out.push_str("[redacted]");
                    rest = &after[end..];
                    continue 'outer;
                }
            }
        }
        let mut chars = rest.chars();
        if let Some(c) = chars.next() {
            out.push(c);
        }
        rest = chars.as_str();
    }
    out
}

// Also handles JSON fragments embedded in log lines, including escaped string values.
fn redact_json_fields(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = String::new();
    let mut pos = 0;
    while pos < bytes.len() {
        if bytes[pos] != b'"' {
            let c = text[pos..].chars().next().unwrap_or(' ');
            out.push(c);
            pos += c.len_utf8();
            continue;
        }
        let start = pos;
        pos += 1;
        while pos < bytes.len() {
            if bytes[pos] == b'\\' {
                pos = (pos + 2).min(bytes.len());
            } else if bytes[pos] == b'"' {
                pos += 1;
                break;
            } else {
                pos += 1;
            }
        }
        let key_end = pos;
        let key = serde_json::from_str::<String>(&text[start..key_end])
            .unwrap_or_default()
            .to_ascii_lowercase();
        while pos < bytes.len() && bytes[pos].is_ascii_whitespace() {
            pos += 1;
        }
        if bytes.get(pos) != Some(&b':')
            || !["key", "token", "secret"].iter().any(|k| key.contains(k))
        {
            out.push_str(&text[start..pos]);
            continue;
        }
        pos += 1;
        while pos < bytes.len() && bytes[pos].is_ascii_whitespace() {
            pos += 1;
        }
        out.push_str(&text[start..pos]);
        // A secret-valued object/array is consumed as a whole using serde's stream offset.
        let mut stream =
            serde_json::Deserializer::from_str(&text[pos..]).into_iter::<serde_json::Value>();
        if stream.next().is_some_and(|v| v.is_ok()) {
            pos += stream.byte_offset();
            out.push_str("\"[redacted]\"");
        } else {
            // Incomplete JSON must not reveal a partial credential.
            out.push_str("\"[redacted]\"");
            break;
        }
    }
    out
}

/// Stream lines through the shared redactor before the first durable write. Lines over
/// 64 KiB are discarded entirely, so a split secret is never persisted in a partial chunk.
pub fn copy_redacted(mut input: impl std::io::Read, mut output: impl Write) -> std::io::Result<()> {
    let mut chunk = [0u8; 4096];
    let mut line = Vec::new();
    let mut oversized = false;
    loop {
        let n = input.read(&mut chunk)?;
        if n == 0 {
            break;
        }
        for b in &chunk[..n] {
            if *b == b'\n' {
                if oversized {
                    output.write_all(b"[redacted oversized log line]\n")?;
                } else {
                    output.write_all(redact(&String::from_utf8_lossy(&line)).as_bytes())?;
                    output.write_all(b"\n")?;
                }
                output.flush()?;
                line.clear();
                oversized = false;
            } else if !oversized {
                if line.len() == 65536 {
                    line.clear();
                    oversized = true;
                } else {
                    line.push(*b);
                }
            }
        }
    }
    if oversized {
        output.write_all(b"[redacted oversized log line]")?;
    } else {
        output.write_all(redact(&String::from_utf8_lossy(&line)).as_bytes())?;
    }
    output.flush()
}

/// A `tracing-subscriber` writer to standard error that redacts each write.
#[derive(Debug, Clone, Copy, Default)]
pub struct RedactingStderr;

/// One write handle of [`RedactingStderr`].
#[derive(Debug, Default)]
pub struct RedactingWriter {
    buf: Vec<u8>,
}

impl Write for RedactingWriter {
    fn write(&mut self, data: &[u8]) -> std::io::Result<usize> {
        self.buf.extend_from_slice(data);
        Ok(data.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        if self.buf.is_empty() {
            return Ok(());
        }
        let text = String::from_utf8_lossy(&self.buf).into_owned();
        self.buf.clear();
        let mut err = std::io::stderr().lock();
        err.write_all(redact(&text).as_bytes())?;
        err.flush()
    }
}

impl Drop for RedactingWriter {
    fn drop(&mut self) {
        let _ = self.flush();
    }
}

impl<'a> tracing_subscriber::fmt::MakeWriter<'a> for RedactingStderr {
    type Writer = RedactingWriter;

    fn make_writer(&'a self) -> Self::Writer {
        RedactingWriter::default()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn r2_19_stream_and_json_secrets_are_redacted_before_write() {
        let raw = br#"{"nested":{"apiKey":"plain-password","secret":{"value":123}},"message":"sk-abcdefgh"}
Bearer abcdef
"#;
        let mut output = Vec::new();
        copy_redacted(&raw[..], &mut output).unwrap();
        let text = String::from_utf8(output).unwrap();
        for secret in ["plain-password", "123", "abcdefgh", "abcdef"] {
            assert!(!text.contains(secret), "{text}");
        }
    }

    #[test]
    fn keys_tokens_and_bearers_are_redacted() {
        assert_eq!(
            redact("key etk_0123456789abcdef in use"),
            "key etk_[redacted] in use"
        );
        assert_eq!(redact("ticket=ett_abcdEFGH12"), "ticket=ett_[redacted]");
        assert_eq!(
            redact("Authorization: Bearer abc.def-123"),
            "Authorization: Bearer [redacted]"
        );
        assert_eq!(
            redact("authorization: bearer xyz"),
            "authorization: bearer [redacted]"
        );
        assert_eq!(redact("etp_welcome"), "etp_[redacted]");
        // Short words that merely start like a prefix stay.
        assert_eq!(redact("etk_ab"), "etk_ab");
        assert_eq!(redact("plain text, ünïcode"), "plain text, ünïcode");
    }
}
