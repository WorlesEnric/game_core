# Vendored: etos Rust SDK

`vendor/etos-sdk/` is a copy of `sdk/rust` from the **etos** repository, used by
`studio/agent` as `etos-sdk = { path = "vendor/etos-sdk" }`. Do not edit it by hand:
refresh it with `studio/agent/vendor-etos-sdk.sh <etos-checkout>`.

| | |
|---|---|
| Source repository | the etos repository, directory `sdk/rust` |
| Commit | `6c2c3f4ea238bd211f9c3e8e9a813b22a13bc9a8` |
| Commit subject | sdk update |
| Commit date | 2026-10-05T00:54:48+08:00 |
| Selected by | ETOS_COMMIT |
| Crate | `etos-sdk` 1.0.0 |
| Licence | Apache-2.0 (declared in the crate's `Cargo.toml`; the repository carries no LICENSE file at this commit) |
| Copied | src/, Cargo.toml (trimmed), README.md |
| Left out | `examples/`, `tests/`, `target/`, `clippy.toml`, `.gitignore`; the `[dev-dependencies]` and `[[example]]` sections of `Cargo.toml` |
