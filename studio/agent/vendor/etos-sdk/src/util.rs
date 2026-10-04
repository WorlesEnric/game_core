//! Small shared helpers: canonical JSON, digests, ids, path segments, time.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

use serde_json::Value;
use sha2::{Digest, Sha256};

/// JSON with object keys sorted, the same rule as the node's canonical JSON.
pub(crate) fn canonical_json(v: &Value) -> String {
    let mut out = String::new();
    write_canonical(v, &mut out);
    out
}

fn write_canonical(v: &Value, out: &mut String) {
    match v {
        Value::Object(map) => {
            out.push('{');
            let mut keys: Vec<&String> = map.keys().collect();
            keys.sort();
            for (i, k) in keys.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                out.push_str(&Value::String((*k).clone()).to_string());
                out.push(':');
                if let Some(child) = map.get(*k) {
                    write_canonical(child, out);
                }
            }
            out.push('}');
        }
        Value::Array(items) => {
            out.push('[');
            for (i, item) in items.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                write_canonical(item, out);
            }
            out.push(']');
        }
        other => out.push_str(&other.to_string()),
    }
}

/// `sha256:<hex>` of a text.
pub(crate) fn sha256(text: &str) -> String {
    format!("sha256:{}", hex::encode(Sha256::digest(text.as_bytes())))
}

/// Milliseconds since the Unix epoch.
pub(crate) fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| i64::try_from(d.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

/// Random bytes; a zeroed buffer only if the OS source fails, which callers tolerate (ids stay
/// unique through their time prefix).
fn random<const N: usize>() -> [u8; N] {
    let mut buf = [0u8; N];
    if getrandom::fill(&mut buf).is_err() {
        buf = [0u8; N];
    }
    buf
}

/// A fresh id: `<prefix>_<time-ordered hex><random hex>`.
pub(crate) fn new_id(prefix: &str) -> String {
    format!("{prefix}_{:012x}{}", now_ms(), hex::encode(random::<8>()))
}

/// `base` scaled by a random factor in [0.5, 1.0), so reconnecting clients spread out.
pub(crate) fn jitter(base: Duration) -> Duration {
    let [b] = random::<1>();
    base.mul_f64(0.5 + f64::from(b) / 512.0)
}

/// Percent-encode one path segment or query value (everything but RFC 3986 unreserved
/// characters and `:`).
pub(crate) fn seg(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for b in value.bytes() {
        if b.is_ascii_alphanumeric() || matches!(b, b'-' | b'.' | b'_' | b'~' | b':') {
            out.push(char::from(b));
        } else {
            out.push_str(&format!("%{b:02X}"));
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn canonical_json_sorts_keys_at_every_level() {
        let v = json!({"b": 1, "a": {"d": [true, null], "c": "x\"y"}});
        assert_eq!(
            canonical_json(&v),
            r#"{"a":{"c":"x\"y","d":[true,null]},"b":1}"#
        );
        assert!(sha256("").starts_with("sha256:e3b0c442"));
    }

    #[test]
    fn segments_are_percent_encoded() {
        assert_eq!(seg("model:default"), "model:default");
        assert_eq!(seg("a b/c"), "a%20b%2Fc");
        assert!(new_id("req").starts_with("req_"));
        let j = jitter(Duration::from_millis(1000));
        assert!(j >= Duration::from_millis(500) && j < Duration::from_millis(1000));
    }
}
