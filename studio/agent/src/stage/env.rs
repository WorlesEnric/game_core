//! The environment of every stage child process (04 §1: the companion inherits etosd's
//! environment, provider keys included; nothing staged may see it).
//!
//! A child gets `env_clear()` plus the allowlisted names below, minus anything whose name
//! looks like a secret. The Unity Editor needs `HOME` (its licence and caches) and `UNITY`;
//! dotnet needs `DOTNET_*`/`NUGET_PACKAGES`; the shared Unity lock reads `GC_STUDIO_*`.

/// Exact names passed to stage children.
pub const ENV_ALLOW: &[&str] = &[
    "PATH",
    "HOME",
    "USER",
    "LOGNAME",
    "LANG",
    "LC_ALL",
    "TMPDIR",
    "DISPLAY",
    "SHELL",
    "XDG_RUNTIME_DIR",
    "UNITY",
    "NUGET_PACKAGES",
];

/// Name prefixes passed to stage children (when the name does not look like a secret).
pub const ENV_ALLOW_PREFIXES: &[&str] = &["GAMECORE_", "UNITY_", "DOTNET_", "GC_STUDIO_"];

/// Name fragments that mark a secret whatever the prefix.
const SECRET_FRAGMENTS: &[&str] = &[
    "KEY",
    "TOKEN",
    "SECRET",
    "PASSWORD",
    "PASSWD",
    "CREDENTIAL",
    "AUTH",
    "COOKIE",
    "SESSION",
    "PRIVATE",
];

/// True when an environment variable's name looks like it holds a secret.
pub fn secret_like(name: &str) -> bool {
    let n = name.to_ascii_uppercase();
    SECRET_FRAGMENTS.iter().any(|s| n.contains(s))
}

/// True when `name` may be passed to a stage child.
pub fn allowed(name: &str) -> bool {
    (ENV_ALLOW.contains(&name) || ENV_ALLOW_PREFIXES.iter().any(|p| name.starts_with(p)))
        && !secret_like(name)
}

/// The allowlisted subset of `vars`.
pub fn filter_env(vars: impl IntoIterator<Item = (String, String)>) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = vars.into_iter().filter(|(k, _)| allowed(k)).collect();
    out.sort();
    out
}

/// The environment a stage child gets (from this process's environment).
pub fn stage_env() -> Vec<(String, String)> {
    filter_env(std::env::vars())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn vars(names: &[&str]) -> Vec<(String, String)> {
        names
            .iter()
            .map(|n| (n.to_string(), "v".to_string()))
            .collect()
    }

    #[test]
    fn provider_keys_and_tokens_never_reach_a_stage_child() {
        let env = filter_env(vars(&[
            "PATH",
            "HOME",
            "ECHO_API_KEY",
            "BAILIAN_API_KEY",
            "DASHSCOPE_API_KEY",
            "OPENAI_API_KEY",
            "ETOS_KEY_FILE",
            "ETOS_URL",
            "ETOS_STATE_DIR",
            "GAMECORE_TOKEN",
            "GAMECORE_STAGE_ROOT",
            "UNITY",
            "UNITY_SILENCE_TIMEOUT",
            "UNITY_LICENSE_PASSWORD",
            "DOTNET_ROOT",
            "DOTNET_AUTH_HEADER",
            "GC_STUDIO_UNITY_SLOTS",
            "AWS_SECRET_ACCESS_KEY",
            "HTTP_PROXY",
            "SSH_AUTH_SOCK",
        ]));
        let names: Vec<&str> = env.iter().map(|(k, _)| k.as_str()).collect();
        assert_eq!(
            names,
            vec![
                "DOTNET_ROOT",
                "GAMECORE_STAGE_ROOT",
                "GC_STUDIO_UNITY_SLOTS",
                "HOME",
                "PATH",
                "UNITY",
                "UNITY_SILENCE_TIMEOUT",
            ]
        );
    }

    #[test]
    fn secret_names_are_recognised_case_insensitively() {
        for name in [
            "ECHO_API_KEY",
            "gamecore_token",
            "My_Secret",
            "DB_PASSWORD",
            "NPM_AUTH",
            "X_CREDENTIALS",
        ] {
            assert!(secret_like(name), "{name}");
        }
        for name in ["GAMECORE_SLOT_ROOT", "PATH", "UNITY", "DOTNET_ROOT"] {
            assert!(!secret_like(name), "{name}");
        }
    }

    #[test]
    fn this_process_environment_is_filtered() {
        assert!(stage_env().iter().all(|(k, _)| allowed(k)));
        assert!(stage_env().iter().all(|(k, _)| !secret_like(k)));
    }
}
