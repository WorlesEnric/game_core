//! Small helpers: time, ids, digests, canonical JSON, names etos accepts.

use std::time::{SystemTime, UNIX_EPOCH};

use serde_json::Value;
use sha2::{Digest, Sha256};

/// Milliseconds since the Unix epoch.
pub fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| i64::try_from(d.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

/// A fresh id `<prefix>_<time hex><random hex>`.
pub fn new_id(prefix: &str) -> String {
    let mut buf = [0u8; 6];
    if getrandom::fill(&mut buf).is_err() {
        buf = [0u8; 6];
    }
    format!("{prefix}_{:011x}{}", now_ms(), hex::encode(buf))
}

/// Lowercase hex SHA-256 of bytes.
pub fn sha256_hex(bytes: &[u8]) -> String {
    hex::encode(Sha256::digest(bytes))
}

/// A digest as written by workers or etos (`sha256:<hex>` or `<hex>`, any case) normalised
/// to 64 lowercase hex characters; `None` when it is not a SHA-256 digest.
pub fn normalize_sha256(text: &str) -> Option<String> {
    let t = text.trim();
    let t = t.strip_prefix("sha256:").unwrap_or(t);
    (t.len() == 64 && t.chars().all(|c| c.is_ascii_hexdigit())).then(|| t.to_ascii_lowercase())
}

/// JSON with object keys sorted at every level (the rule etos uses for digests).
pub fn canonical_json(v: &Value) -> String {
    fn write(v: &Value, out: &mut String) {
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
                        write(child, out);
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
                    write(item, out);
                }
                out.push(']');
            }
            other => out.push_str(&other.to_string()),
        }
    }
    let mut out = String::new();
    write(v, &mut out);
    out
}

/// A change-set id of the contract (03 §6, P0.3's `IdDerivation.NewChangeSetId`): `cs_` plus
/// a 26-character Crockford ULID in upper case (`^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$`).
pub fn valid_change_set_id(id: &str) -> bool {
    let Some(ulid) = id.strip_prefix("cs_") else {
        return false;
    };
    ulid.len() == 26
        && matches!(ulid.as_bytes()[0], b'0'..=b'7')
        && ulid.bytes().all(|b| {
            b.is_ascii_digit()
                || (b.is_ascii_uppercase() && !matches!(b, b'I' | b'L' | b'O' | b'U'))
        })
}

/// One topic segment etos accepts (lowercase letters, digits, `-`, `_`, `.`; at most 64)
/// derived from a change-set id and an attempt number: `cs-<id>` when the id already fits,
/// else a sanitised prefix plus 8 hex characters of the id's digest, so distinct ids never
/// collide. Attempts after the first get `-r<n>`.
pub fn topic_segment(change_set_id: &str, attempt: u32) -> String {
    let fits = change_set_id
        .chars()
        .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || matches!(c, '-' | '_' | '.'))
        && change_set_id.len() <= 48;
    let base = if valid_change_set_id(change_set_id) {
        // Crockford base32 is case-insensitive: lower-casing keeps ids distinct.
        format!("cs-{}", change_set_id.to_ascii_lowercase())
    } else if fits {
        format!("cs-{change_set_id}")
    } else {
        let clean: String = change_set_id
            .chars()
            .map(|c| {
                let c = c.to_ascii_lowercase();
                if c.is_ascii_lowercase() || c.is_ascii_digit() || matches!(c, '-' | '_' | '.') {
                    c
                } else {
                    '-'
                }
            })
            .take(40)
            .collect();
        let digest = sha256_hex(change_set_id.as_bytes());
        format!("cs-{clean}-{}", &digest[..8])
    };
    if attempt == 0 {
        base
    } else {
        format!("{base}-r{attempt}")
    }
}

/// A media type guessed from a file name's extension.
pub fn media_type_for(name: &str) -> &'static str {
    let ext = name
        .rsplit_once('.')
        .map(|(_, e)| e.to_ascii_lowercase())
        .unwrap_or_default();
    match ext.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "gif" => "image/gif",
        "wav" => "audio/wav",
        "mp3" => "audio/mpeg",
        "ogg" => "audio/ogg",
        "flac" => "audio/flac",
        "glb" => "model/gltf-binary",
        "gltf" => "model/gltf+json",
        "obj" => "model/obj",
        "fbx" => "application/octet-stream",
        "json" => "application/json",
        "md" => "text/markdown",
        "txt" => "text/plain",
        "tgz" | "gz" => "application/gzip",
        "tar" => "application/x-tar",
        "zip" => "application/zip",
        "mp4" => "video/mp4",
        _ => "application/octet-stream",
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn digests_normalise() {
        let h = sha256_hex(b"");
        assert_eq!(
            h,
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        );
        assert_eq!(
            normalize_sha256(&format!("sha256:{}", h.to_uppercase())),
            Some(h.clone())
        );
        assert_eq!(normalize_sha256("sha256:abc"), None);
    }

    #[test]
    fn topic_segments_are_valid_and_distinct() {
        assert_eq!(topic_segment("cs_01abc", 0), "cs-cs_01abc");
        assert_eq!(topic_segment("cs_01abc", 1), "cs-cs_01abc-r1");
        assert_eq!(
            topic_segment("cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXY", 1),
            "cs-cs_01j9zq3k4m5n6p7q8r9s0tvwxy-r1"
        );
        let a = topic_segment("cs_01J9ZZ", 0);
        let b = topic_segment("cs_01j9zz:", 0);
        assert_ne!(a, b);
        for s in [a, b, topic_segment(&"X".repeat(96), 3)] {
            assert!(s.len() <= 64, "{s}");
            assert!(s.chars().all(|c| c.is_ascii_lowercase()
                || c.is_ascii_digit()
                || matches!(c, '-' | '_' | '.')));
        }
    }

    #[test]
    fn canonical_json_sorts_keys() {
        assert_eq!(
            canonical_json(&json!({"b": 1, "a": {"d": [true], "c": "x"}})),
            r#"{"a":{"c":"x","d":[true]},"b":1}"#
        );
        assert!(valid_change_set_id("cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXY"));
        for bad in [
            "cs 1",
            "cs_01J9.a:b-c",
            "cs_01j9zq3k4m5n6p7q8r9s0tvwxy",
            "cs_81J9ZQ3K4M5N6P7Q8R9S0TVWXY",
            "cs_01J9ZQ3K4M5N6P7Q8R9S0TVWXI",
            "cs_01J9ZQ3K4M5N6P7Q8R9S0TVWX",
        ] {
            assert!(!valid_change_set_id(bad), "{bad}");
        }
        assert!(new_id("vs").starts_with("vs_"));
    }
}
