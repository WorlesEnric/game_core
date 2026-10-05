//! The environment of every stage child process (04 §1: the companion inherits etosd's
//! environment, provider keys included; nothing staged may see it).
//!
//! Children receive only LANG/LC_ALL and a fixed system PATH. Trusted launch code sets
//! slot-local HOME and required tool/cache paths explicitly. No inherited runtime hooks,
//! tool overrides, broad prefix matches, proxy settings or credentials pass through.

/// Exact names passed to stage children.
pub const ENV_ALLOW: &[&str] = &["LANG", "LC_ALL"];

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
    ENV_ALLOW.contains(&name) && !secret_like(name)
}

/// The allowlisted subset of `vars`.
pub fn filter_env(vars: impl IntoIterator<Item = (String, String)>) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = vars.into_iter().filter(|(k, _)| allowed(k)).collect();
    out.sort();
    out
}

/// The environment a stage child gets (from this process's environment).
pub fn stage_env() -> Vec<(String, String)> {
    {
        let mut env = filter_env(std::env::vars());
        env.push(("PATH".into(), "/usr/local/bin:/usr/bin:/bin".into()));
        env
    }
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
        assert!(names.is_empty());
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
        assert!(stage_env().iter().all(|(k, _)| allowed(k) || k == "PATH"));
        assert!(stage_env().iter().all(|(k, _)| !secret_like(k)));
    }
}
