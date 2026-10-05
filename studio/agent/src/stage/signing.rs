//! Installation-local HMAC-SHA256 stage attestations. The secret never leaves state_dir.
use std::io::Write;
use std::os::unix::fs::{OpenOptionsExt, PermissionsExt};
use std::path::Path;

use serde_json::Value;
use sha2::{Digest, Sha256};

use crate::util::canonical_json;

fn installation_secret(state: &Path) -> Result<Vec<u8>, String> {
    std::fs::create_dir_all(state).map_err(|e| e.to_string())?;
    let path = state.join("stage-signing-secret");
    if !path.exists() {
        let mut bytes = [0u8; 32];
        getrandom::fill(&mut bytes).map_err(|e| e.to_string())?;
        let tmp = state.join(format!(".signing-{}", crate::util::new_id("tmp")));
        let result = (|| {
            let mut file = std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .mode(0o600)
                .open(&tmp)
                .map_err(|e| e.to_string())?;
            file.write_all(&bytes)
                .and_then(|()| file.sync_all())
                .map_err(|e| e.to_string())?;
            match std::fs::hard_link(&tmp, &path) {
                Ok(()) => std::fs::File::open(state)
                    .and_then(|f| f.sync_all())
                    .map_err(|e| e.to_string()),
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => Ok(()),
                Err(e) => Err(e.to_string()),
            }
        })();
        let _ = std::fs::remove_file(tmp);
        result?;
    }
    let metadata = std::fs::symlink_metadata(&path).map_err(|e| e.to_string())?;
    if !metadata.is_file() || metadata.permissions().mode() & 0o777 != 0o600 {
        return Err("stage signing secret must be a regular 0600 file".into());
    }
    let bytes = std::fs::read(path).map_err(|e| e.to_string())?;
    if bytes.len() != 32 {
        return Err("invalid stage signing secret length".into());
    }
    Ok(bytes)
}

pub(crate) fn hmac(key: &[u8], message: &[u8]) -> Vec<u8> {
    let key = if key.len() > 64 {
        Sha256::digest(key).to_vec()
    } else {
        key.to_vec()
    };
    let mut inner = [0x36u8; 64];
    let mut outer = [0x5cu8; 64];
    for (i, b) in key.iter().enumerate() {
        inner[i] ^= b;
        outer[i] ^= b;
    }
    let mut digest = Sha256::new();
    digest.update(inner);
    digest.update(message);
    let mut result = Sha256::new();
    result.update(outer);
    result.update(digest.finalize());
    result.finalize().to_vec()
}

/// Sign a complete trusted service record (never accepts candidate-supplied signatures).
pub fn sign(state: &Path, mut record: Value) -> Result<Value, String> {
    let object = record.as_object_mut().ok_or("verdict must be an object")?;
    object.remove("signature");
    let signature = hex::encode(hmac(
        &installation_secret(state)?,
        canonical_json(&record).as_bytes(),
    ));
    record["signature"] = Value::String(signature);
    Ok(record)
}

/// Authenticate every field with constant-time signature comparison.
pub fn verify(state: &Path, record: &Value) -> Result<bool, String> {
    let Some(signature) = record["signature"]
        .as_str()
        .and_then(|s| hex::decode(s).ok())
    else {
        return Ok(false);
    };
    let mut unsigned = record.clone();
    let Some(object) = unsigned.as_object_mut() else {
        return Ok(false);
    };
    object.remove("signature");
    let expected = hmac(
        &installation_secret(state)?,
        canonical_json(&unsigned).as_bytes(),
    );
    Ok(signature.len() == expected.len()
        && signature
            .iter()
            .zip(expected)
            .fold(0u8, |diff, (a, b)| diff | (a ^ b))
            == 0)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn r2_09_hmac_rfc4231_and_installation_binding() {
        assert_eq!(
            hex::encode(hmac(&[0x0b; 20], b"Hi There")),
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"
        );
        let first = tempfile::tempdir().unwrap();
        let second = tempfile::tempdir().unwrap();
        let v = sign(
            first.path(),
            serde_json::json!({"jobId":"j","projectId":"p","steps":[]}),
        )
        .unwrap();
        assert!(verify(first.path(), &v).unwrap());
        assert!(!verify(second.path(), &v).unwrap());
        let mut tampered = v.clone();
        tampered["projectId"] = "other".into();
        assert!(!verify(first.path(), &tampered).unwrap());
        assert_eq!(sign(first.path(), v.clone()).unwrap(), v);
    }
}
