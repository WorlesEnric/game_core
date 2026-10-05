# Vendored: etos Rust SDK

`vendor/etos-sdk/` is a copy of `sdk/rust` from the **etos** repository, used by
`studio/agent` as `etos-sdk = { path = "vendor/etos-sdk" }`. Do not edit it by hand:
refresh it with `studio/agent/vendor-etos-sdk.sh <etos-checkout>`.

| | |
|---|---|
| Source repository | the etos repository, directory `sdk/rust` |
| Commit | `278ef9cf421f5e64e83a402960def0c51e3833c3` |
| Commit subject | etagents: POST /tasks records the request's task even when the caller stops waiting |
| Commit date | 2026-10-04T15:58:32-07:00 |
| Selected by | studio/etos/etos.lock |
| Crate | `etos-sdk` 1.0.0 |
| Licence | Apache-2.0 (declared in the crate's `Cargo.toml`; the repository carries no LICENSE file at this commit) |
| Copied | src/, Cargo.toml (trimmed), README.md |
| Left out | `examples/`, `tests/`, `target/`, `clippy.toml`, `.gitignore`; the `[dev-dependencies]` and `[[example]]` sections of `Cargo.toml` |
