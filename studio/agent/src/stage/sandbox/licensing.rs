//! A fresh, disposable Unity HOME for each confined process; never a writable host mount.
use std::fs;
use std::os::unix::fs::{DirBuilderExt, OpenOptionsExt, PermissionsExt};
use std::path::{Component, Path, PathBuf};

use super::Sandbox;

pub(super) struct LicenseHome(pub PathBuf);

impl Drop for LicenseHome {
    fn drop(&mut self) {
        // remove_dir_all does not follow links created by the confined process.
        // A killed wrapper may leave this directory; it is still inside the job for GC.
        let _ = fs::remove_dir_all(&self.0);
    }
}

pub(super) fn mount_path(path: &Path) -> Result<(), String> {
    if !path.is_absolute()
        || path.to_string_lossy().contains([',', ':'])
        || path.components().any(|c| matches!(c, Component::ParentDir))
    {
        return Err("sandbox paths must be absolute without traversal or mount delimiters".into());
    }
    Ok(())
}

pub(super) fn no_links(path: &Path) -> Result<(), String> {
    for ancestor in path.ancestors() {
        match fs::symlink_metadata(ancestor) {
            Ok(m) if m.file_type().is_symlink() => {
                return Err("Unity licence paths must not contain symbolic links".into());
            }
            Ok(_) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(e.to_string()),
        }
    }
    Ok(())
}

fn copy_state(source: &Path, target: &Path) -> Result<(), String> {
    no_links(source)?;
    let metadata = match fs::symlink_metadata(source) {
        Ok(m) => m,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e.to_string()),
    };
    if metadata.is_dir() {
        fs::create_dir_all(target).map_err(|e| e.to_string())?;
        fs::set_permissions(target, fs::Permissions::from_mode(0o700))
            .map_err(|e| e.to_string())?;
        for entry in fs::read_dir(source).map_err(|e| e.to_string())? {
            let entry = entry.map_err(|e| e.to_string())?;
            let name = entry.file_name();
            let name_text = name.to_string_lossy();
            // Never read keys or unrelated credential files, including in operator state.
            if name_text.ends_with(".key")
                || matches!(name_text.as_ref(), "auth.json" | "providers.env")
            {
                continue;
            }
            copy_state(&entry.path(), &target.join(name))?;
        }
    } else if metadata.is_file() {
        fs::create_dir_all(target.parent().ok_or("licence target has no parent")?)
            .map_err(|e| e.to_string())?;
        let mut input = fs::File::open(source).map_err(|e| e.to_string())?;
        let mut output = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(0o600)
            .open(target)
            .map_err(|e| e.to_string())?;
        std::io::copy(&mut input, &mut output).map_err(|e| e.to_string())?;
    } else {
        return Err("Unity licence state must contain only regular files and directories".into());
    }
    Ok(())
}

// Called only after manifest verification. Copy into disposable private HOME on every
// Unity launch so neither another job nor a previous candidate can supply UPM inputs.
pub(super) fn copy_unity_cache(sandbox: &Sandbox, private_home: &Path) -> Result<(), String> {
    copy_state(&sandbox.cache.join("upm"), &private_home.join(".cache/upm"))
}

impl LicenseHome {
    pub(super) fn prepare(sandbox: &Sandbox) -> Result<Self, String> {
        for path in [
            &sandbox.home,
            &sandbox.slot,
            &sandbox.cache,
            &sandbox.editor,
            &sandbox.packages,
        ] {
            mount_path(path)?;
        }
        if sandbox.home == Path::new("/") {
            return Err("sandbox HOME must not be the filesystem root".into());
        }
        no_links(&sandbox.slot)?;
        fs::create_dir_all(&sandbox.slot).map_err(|e| e.to_string())?;
        let mut random = [0u8; 16];
        getrandom::fill(&mut random).map_err(|e| e.to_string())?;
        let path = sandbox
            .slot
            .join(format!(".licensing-{}", hex::encode(random)));
        fs::DirBuilder::new()
            .mode(0o700)
            .create(&path)
            .map_err(|e| e.to_string())?;
        let private = Self(path);
        let config = sandbox.home.join(".config/unity3d/Unity");
        let local = sandbox.home.join(".local/share/unity3d/Unity");
        for source in &sandbox.licences {
            let target = if source == &config {
                private.0.join(".config/unity3d/Unity")
            } else if source == &config.join("licenses") {
                private.0.join(".config/unity3d/Unity/licenses")
            } else if source == &local {
                private.0.join(".local/share/unity3d/Unity")
            } else if source == Path::new("/var/lib/unity") {
                private.0.join(".system-unity")
            } else {
                return Err("unsupported Unity licence directory".into());
            };
            if source == &config || source == &local {
                // Entitlements/configuration only, not logs, telemetry databases or Editor state.
                for name in ["Unity_lic.ulf", "licenses", "config"] {
                    copy_state(&source.join(name), &target.join(name))?;
                }
            } else {
                copy_state(source, &target)?;
            }
        }
        copy_state(
            Path::new("/usr/share/unity3d/config/services-config.json"),
            &private.0.join(".services-config.json"),
        )?;
        for rel in [
            ".config/unity3d/Unity",
            ".local/share/unity3d/Unity",
            ".cache/unity3d",
        ] {
            fs::create_dir_all(private.0.join(rel)).map_err(|e| e.to_string())?;
        }
        // Pre-create nested bind targets as the job's uid, otherwise Docker creates root-owned
        // parents in the private HOME and ordinary job cleanup cannot remove them.
        for target in [
            &sandbox.slot,
            &sandbox.cache,
            &sandbox.editor,
            &sandbox.packages,
        ] {
            if let Ok(relative) = target.strip_prefix(&sandbox.home) {
                fs::create_dir_all(private.0.join(relative)).map_err(|e| e.to_string())?;
            }
        }
        Ok(private)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::unix::fs::{MetadataExt, symlink};

    #[test]
    fn stage_tmp_unity_cache_copy_is_private_and_disposable() {
        let temp = tempfile::tempdir().unwrap();
        let sandbox = fixture(temp.path());
        fs::create_dir_all(sandbox.cache.join("upm")).unwrap();
        fs::write(sandbox.cache.join("upm/pinned"), "verified").unwrap();
        let first = LicenseHome::prepare(&sandbox).unwrap();
        let second = LicenseHome::prepare(&sandbox).unwrap();
        copy_unity_cache(&sandbox, &first.0).unwrap();
        copy_unity_cache(&sandbox, &second.0).unwrap();
        fs::write(first.0.join(".cache/upm/pinned"), "candidate write").unwrap();
        assert_eq!(
            fs::read_to_string(second.0.join(".cache/upm/pinned")).unwrap(),
            "verified"
        );
        assert_eq!(
            fs::read_to_string(sandbox.cache.join("upm/pinned")).unwrap(),
            "verified"
        );
        let removed = first.0.clone();
        drop(first);
        assert!(!removed.exists());
    }

    fn fixture(temp: &Path) -> Sandbox {
        let host = temp.join("host");
        let config = host.join(".config/unity3d/Unity");
        fs::create_dir_all(config.join("licenses")).unwrap();
        fs::create_dir_all(config.join("config")).unwrap();
        fs::write(
            config.join("licenses/UnityEntitlementLicense.xml"),
            "synthetic-entitlement",
        )
        .unwrap();
        fs::write(
            config.join("config/production.json"),
            "synthetic-configuration",
        )
        .unwrap();
        fs::write(
            config.join("Unity.Licensing.Client.log"),
            "not-licence-state",
        )
        .unwrap();
        fs::write(config.join("config/auth.json"), "must-not-copy").unwrap();
        fs::write(config.join("config/test.key"), "must-not-copy").unwrap();
        let mut sandbox = Sandbox::defaults(
            &host.join("scratch/slot"),
            &host.join("scratch/cache"),
            &host.join("repo"),
        );
        sandbox.home = host.clone();
        sandbox.editor = host.join("Unity/Editor");
        sandbox.licences = vec![config];
        sandbox
    }

    #[test]
    fn r2_f2_private_license_copies_are_writable_isolated_and_deleted() {
        let temp = tempfile::tempdir().unwrap();
        let sandbox = fixture(temp.path());
        let first = LicenseHome::prepare(&sandbox).unwrap();
        let second = LicenseHome::prepare(&sandbox).unwrap();
        let rel = ".config/unity3d/Unity/licenses/UnityEntitlementLicense.xml";
        assert_ne!(first.0, second.0);
        assert!(first.0.starts_with(&sandbox.slot));
        assert_eq!(fs::metadata(&first.0).unwrap().mode() & 0o777, 0o700);
        assert_eq!(
            fs::metadata(first.0.join(rel)).unwrap().mode() & 0o777,
            0o600
        );
        assert_ne!(
            fs::metadata(first.0.join(rel)).unwrap().ino(),
            fs::metadata(sandbox.home.join(rel)).unwrap().ino()
        );
        fs::write(first.0.join(rel), "changed-in-container").unwrap();
        assert_eq!(
            fs::read_to_string(second.0.join(rel)).unwrap(),
            "synthetic-entitlement"
        );
        assert_eq!(
            fs::read_to_string(sandbox.home.join(rel)).unwrap(),
            "synthetic-entitlement"
        );
        for excluded in [
            "Unity.Licensing.Client.log",
            "config/auth.json",
            "config/test.key",
        ] {
            assert!(
                !first
                    .0
                    .join(".config/unity3d/Unity")
                    .join(excluded)
                    .exists()
            );
        }
        for target in [
            &sandbox.slot,
            &sandbox.cache,
            &sandbox.editor,
            &sandbox.packages,
        ] {
            let target = first.0.join(target.strip_prefix(&sandbox.home).unwrap());
            assert!(target.is_dir());
            assert_eq!(
                fs::metadata(target).unwrap().uid(),
                fs::metadata(&first.0).unwrap().uid()
            );
        }
        // A confined process cannot make cleanup follow a link into the host's state.
        symlink(&sandbox.home, first.0.join("host-link")).unwrap();
        let path = first.0.clone();
        drop(first);
        assert!(!path.exists());
        assert!(sandbox.home.join(rel).exists());
    }

    #[test]
    fn r2_f2_license_links_and_unapproved_sources_fail_closed_and_clean_up() {
        let temp = tempfile::tempdir().unwrap();
        let mut sandbox = fixture(temp.path());
        let outside = temp.path().join("outside");
        fs::write(&outside, "do-not-read").unwrap();
        symlink(&outside, sandbox.licences[0].join("licenses/link.xml")).unwrap();
        assert!(
            LicenseHome::prepare(&sandbox)
                .err()
                .unwrap()
                .contains("symbolic links")
        );
        assert_eq!(fs::read_dir(&sandbox.slot).unwrap().count(), 0);
        sandbox.licences = vec![sandbox.home.join(".config")];
        assert!(
            LicenseHome::prepare(&sandbox)
                .err()
                .unwrap()
                .contains("unsupported")
        );
        assert_eq!(fs::read_dir(&sandbox.slot).unwrap().count(), 0);
    }
}
