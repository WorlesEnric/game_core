//! The content-addressed artifact store: `ETOS_STATE_DIR/artifacts/sha256/<aa>/<hash>`.
//!
//! A file is written to a temporary name with a random suffix, fsynced, its digest computed
//! from the bytes written and compared with the expected digest (when one is given), then
//! renamed into place and the directory fsynced. A digest already present is not rewritten.

use std::io::Write;
use std::path::{Path, PathBuf};

use crate::util::{normalize_sha256, sha256_hex};

/// Why a store operation failed.
#[derive(Debug, thiserror::Error)]
pub enum StoreError {
    /// Filesystem failure.
    #[error("artifact store io: {0}")]
    Io(#[from] std::io::Error),
    /// The bytes do not have the expected digest.
    #[error("digest mismatch: expected {expected}, got {actual}")]
    Mismatch {
        /// What was claimed.
        expected: String,
        /// What the bytes hash to.
        actual: String,
    },
    /// Not a SHA-256 digest.
    #[error("{0:?} is not a sha256 digest")]
    BadDigest(String),
}

/// The store.
#[derive(Debug, Clone)]
pub struct ArtifactStore {
    root: PathBuf,
}

impl ArtifactStore {
    /// A store rooted at `root` (created on first write).
    pub fn new(root: impl Into<PathBuf>) -> ArtifactStore {
        ArtifactStore { root: root.into() }
    }

    /// The path of a digest (whether or not it exists).
    pub fn path_of(&self, sha256: &str) -> Result<PathBuf, StoreError> {
        let h = normalize_sha256(sha256).ok_or_else(|| StoreError::BadDigest(sha256.into()))?;
        Ok(self.root.join("sha256").join(&h[..2]).join(&h))
    }

    /// Store bytes; returns `(digest, path)`. With `expected`, a mismatch is refused and
    /// nothing is stored.
    pub fn put(
        &self,
        bytes: &[u8],
        expected: Option<&str>,
    ) -> Result<(String, PathBuf), StoreError> {
        let actual = sha256_hex(bytes);
        if let Some(e) = expected {
            let e = normalize_sha256(e).ok_or_else(|| StoreError::BadDigest(e.into()))?;
            if e != actual {
                return Err(StoreError::Mismatch {
                    expected: e,
                    actual,
                });
            }
        }
        let path = self.path_of(&actual)?;
        if path.is_file() {
            return Ok((actual, path));
        }
        let dir = path.parent().unwrap_or(Path::new("."));
        std::fs::create_dir_all(dir)?;
        // A random suffix: concurrent writers of one digest never share a temporary file.
        let mut nonce = [0u8; 8];
        if getrandom::fill(&mut nonce).is_err() {
            nonce = crate::util::now_ms().to_le_bytes();
        }
        let tmp = dir.join(format!(
            ".{actual}.{}.{}.tmp",
            std::process::id(),
            hex::encode(nonce)
        ));
        {
            let mut f = std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&tmp)?;
            f.write_all(bytes)?;
            f.sync_all()?;
        }
        // Verify what reached the disk before it becomes addressable.
        let written = std::fs::read(&tmp)?;
        let check = sha256_hex(&written);
        if check != actual {
            let _ = std::fs::remove_file(&tmp);
            return Err(StoreError::Mismatch {
                expected: actual,
                actual: check,
            });
        }
        std::fs::rename(&tmp, &path)?;
        // Make the rename durable: fsync the directory entry.
        sync_dir(dir)?;
        Ok((actual, path))
    }

    /// The bytes of a digest, when stored.
    pub fn get(&self, sha256: &str) -> Result<Option<Vec<u8>>, StoreError> {
        let path = self.path_of(sha256)?;
        match std::fs::read(&path) {
            Ok(b) => Ok(Some(b)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(e) => Err(e.into()),
        }
    }
}

/// Flush a directory's entries to disk (a no-op where directories cannot be opened).
fn sync_dir(dir: &Path) -> std::io::Result<()> {
    match std::fs::File::open(dir) {
        Ok(d) => d.sync_all().or_else(|e| {
            // Some filesystems refuse fsync on a directory handle.
            if e.kind() == std::io::ErrorKind::InvalidInput || e.kind() == std::io::ErrorKind::PermissionDenied {
                Ok(())
            } else {
                Err(e)
            }
        }),
        Err(_) => Ok(()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn put_verifies_and_addresses_by_content() {
        let dir = tempfile::tempdir().unwrap();
        let s = ArtifactStore::new(dir.path());
        let (h, p) = s.put(b"hello", None).unwrap();
        assert!(p.ends_with(format!("sha256/{}/{h}", &h[..2])));
        assert_eq!(s.get(&h).unwrap().unwrap(), b"hello");
        assert_eq!(s.put(b"hello", Some(&format!("sha256:{h}"))).unwrap().0, h);
        assert!(matches!(
            s.put(b"other", Some(&h)),
            Err(StoreError::Mismatch { .. })
        ));
        assert!(s.get(&"0".repeat(64)).unwrap().is_none());
        assert!(s.get("nope").is_err());
    }
}
