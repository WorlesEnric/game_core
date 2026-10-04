# PACKET P0.1 host-etos

Owner: Opus 5.5. Branch: this worktree's branch (game_core) and `studio/bailian-tts` in
`/Users/yangcao/wkspace/etos` (not pushed). Design: `docs/studio/04-etos-integration.md` §5, §8;
`docs/studio/02-architecture.md` SADR-002/005/018/019/020.

## 1. Built

### etos (branch `studio/bailian-tts`, base `6c2c3f4`, head @ETOS_HEAD@)

| Commit | What |
|---|---|
| `10e2ae0` | etops: `bailian.rs` brought within the workspace lints (15 `Mutex::lock().unwrap()` outside tests, and rustfmt). **Needed because `scripts/check.sh rust` failed at `6c2c3f4` itself** (clippy `unwrap_used`, fmt). Behaviour unchanged except that a poisoned lock is recovered (as `http.rs` does). |
| `6f30728` | etops: provider kind **`bailian-tts`** (SADR-005 a): `src/speech_bailian.rs` (new), wired in `config.rs` (kind, serves `tts`, default base URL `https://dashscope.aliyuncs.com/api/v1`, needs `model`), `service.rs` (build), `lib.rs` (export). POST `services/aigc/multimodal-generation/generation` `{model, input:{text, voice, language_type?, instructions?}}`; uses `output.audio.data` (base64) when present, else downloads `output.audio.url` only from the provider's host or a `download_hosts` suffix (default `aliyuncs.com`; http(s) only; credential never sent there); usage from `usage.characters`; WAV only (`format` ≠ wav and `speed` refused with `bad_args` before sending); HTTP 4xx → `request_rejected`, 429/5xx → `provider_unavailable` (not retried, billed), 200 with error `code` → `job_failed`, disallowed URL → `bad_response`. Tests: `tests/speech_bailian.rs` (own imitation of DashScope: success, inline audio, 400, 429, error code, URL host refusal without download, unsupported format) + unit tests in the module and in `config.rs`. README: provider table, a `bailian-tts` section with its differences from `openai-speech`, Tests section. |
| `f3eda1a` | sdk/rust: `Ops::generate(family, input)` posts `generate.<family>` (was `generate`, which the node answers `unknown_operation`); invalid family (`""`, containing `.` or `/`) → `Error::Invalid`. Test in `tests/agent.rs`; `sdk/rust/README.md` updated. Public API change: the method gains a `family` argument (no callers existed). |
| `3e8b336` | etops: media type of an extensionless input from its signature bytes (`files.rs`). **Outside the packet's listed etos paths; found by this packet's W0 verification** (SADR-005 b: a verification row proves a gap): every agent op whose input is a pinned reference (`describe`, `ocr`, `transcribe`, generation `references`) passes a content-addressed store path without extension, which `media_of()` refused (`unsupported_input`; first verify run, verbatim in §3). Unit test `media_by_signature_without_an_extension`; README sentence. Separate commit so it can be taken or dropped on its own. |

Line budget: etops @ETOPS_LINES@ / 8000 non-blank lines (`scripts/check.sh` budget table).

### Host (`myubuntu`, user `worlesenric`)

| Item | Path / value |
|---|---|
| etos source at the pin | `~/wkspace/etos-studio/` (rsync of the etos repo without `.git`/`target`; `ETOS_COMMIT` holds sha, branch, base). `~/wkspace/etos` on the host (unrelated) is untouched. |
| binaries | `~/.local/opt/etos/bin/etosd`, `etos` (glibc), `etos-musl` (static-pie musl, also at `<root>/bin/etos`); sha256 in `studio/etos/etos.lock` |
| node root | **`~/.local/share/etos-studio`** (`etosd init --name studio --owner worlesenric`, not `--profile local`). Deviation from the brief's `~/.local/share/etos`: that directory already exists on this host and belongs to an unrelated etos installation (`personal/`, `personal-backend/`, `backups/`, served by the running user unit `etos-node.service`); sharing it would mix the two. Override with `ETOS_STUDIO_ROOT`. |
| config | `<root>/etos.toml`, `models.toml`, `ops.toml` rendered from `studio/etos/*.tmpl` (only `@NODE_NAME@`/`@OWNER@` substituted; no secrets) |
| SDK API | `127.0.0.1:7410` (`[api] listen`), agent budget default 10 USD |
| broker | listen and authority `172.17.0.1:7411` (docker0) |
| web UI | embedded gateway `127.0.0.1:7400` |
| provider env | `~/.config/gamecore-studio/providers.env`, mode 0600, `ECHO_API_KEY`, `BAILIAN_API_KEY`, `DEEPSEEK_API_KEY`, generated on the host by `studio/tools/host-providers-env.sh` (never prints values) |
| service | user unit `~/.config/systemd/user/etosd.service` (`EnvironmentFile=` the file above, `ExecStart=~/.local/opt/etos/bin/etosd run --root <root>`, desktop proxy variables unset), enabled, linger on (it already was) |
| images | `localhost/etos-default:latest` (etos `image/default`, layer version @LAYER@), `localhost/gc-designer:current` (Debian trixie-slim + bash, coreutils, git, tmux, python3, jq, imagemagick 7, ffmpeg 7), `localhost/gc-mechanic:current` (gc-designer + .NET SDK 8.0.425 from `mcr.microsoft.com/dotnet/sdk:8.0@sha256:78235e09…`, NuGet cache `/opt/nuget/packages` pre-restored for a netstandard2.1 library + NUnit test project: Newtonsoft.Json 13.0.3, NUnit 3.14.0, NUnit3TestAdapter 4.5.0, Microsoft.NET.Test.Sdk 17.11.1) |
| workers | `gc-designer`, `gc-mechanic`: `etos worker create <w> --image localhost/<w>:current --network offline --model default --instructions "…"` |
| models | `echo/claude-opus-5-5` alias `default`, `echo/gpt-6-sol` alias `fast`, `echo/gpt-5.6-sol` alias `describe` (Echo endpoint, `credential = "env:ECHO_API_KEY"`) |
| ops | image `echo-images` (`openai-images`, `gpt-image-2`, `{quality="low", response_format="b64_json"}`), describe `echo-describe` (`chat`, `echo/gpt-5.6-sol`), tts `bailian-tts` (`qwen3-tts-flash`, voice Cherry, English), realtime `studio-voice` (`bailian-omni`, `qwen3-omni-flash-realtime`, `semantic_vad`, threshold 0.5); 3d absent (documented in `ops.toml.tmpl`) |
| app | `studio/etos/app/app.toml` (`gamecore-unity`, routes proxy/query/changes/entrances, uses gamecore-studio): **not installed** (needs the P0.5 agent first) |

Other host changes: `cargo-deny` installed into `~/.cargo/bin` (for the etos deny gate);
`rust:1.97.1-alpine` pulled; a cargo registry cache for the musl build at
`~/.cache/gamecore-studio/cargo-musl`.

### game_core files

`studio/etos/{etos.toml.tmpl, models.toml.tmpl, ops.toml.tmpl, etosd.service.tmpl, install.sh,
verify.sh, etos.lock, app/app.toml, verify-agent/agent.toml, verify-agent/realtime_probe.py}`,
`studio/images/{gc-designer/Dockerfile, gc-mechanic/Dockerfile, gc-mechanic/prewarm/**}`,
`studio/tools/{host-sync-etos.sh, host-sync-studio.sh, host-build-etos.sh, host-build-images.sh,
host-providers-env.sh}`, `artifacts/studio/environment/{provider-probe-2026-10-04.md,
etos-verify-2026-10-04/}`.

## 2. Verified (how)

@VERIFIED@

## 3. Refusals and deviations (verbatim)

- `etos worker create gc-allowlist-probe --image localhost/gc-designer:current --network allowlist: --model default`
  → `error: invalid network policy `allowlist:`: use `open`, `allowlist:<host>,<host>` or `offline`` (etos
  `crates/etbroker/src/db.rs` `NetworkPolicy::from_str`, test `policies_round_trip`). The workers use
  `--network offline` (no egress at all), which is what 04 §4 intends ("none needed; ops run on the node").
- Echo `claude-opus-5-5` (the `default` alias, the workers' model) did not answer at probe time:
  HTTP 401 `OAuth access token has been revoked.`, later HTTP 503 `auth_unavailable: no auth available
  (providers=claude, model=claude-opus-5-5); …` (full bodies in `provider-probe-2026-10-04.md`). Configured as
  specified, not substituted. A worker task on `default` will fail until Echo restores it.
- First verify run (before `3e8b336`): `describe` of the generated image → HTTP 4xx `{"code":
  "unsupported_input","message":"image description cannot read `…/store/sha256/2c/4351…`: this provider accepts
  PNG, JPEG, GIF or WebP images (and PDFs for ocr)"}`. Fixed by `3e8b336`.
- First verify run: one `tts` → job `failed`, `provider_unavailable: cannot connect (error sending request for url
  (https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation))`, right after the node's
  first start; the same call succeeded a minute later and in every later run. Not reproduced; noted because a
  billed create is not retried even when the connection was never established.
- 3D: `POST /ops/generate.3d` → HTTP 503 `{"code":"not_configured","message":"3D model generation is not
  available on this node: no provider is configured", …}` (expected, SADR-020).
- `~/.local/share/etos-studio` instead of `~/.local/share/etos` (see §1).
- `studio/etos/app/app.toml` is listed under P0.5's paths in 06 §2; it was written here because this packet's
  brief asks for it. P0.5 may replace it.

## 4. Remaining

- P0.5: `studio/etos/agent/agent.toml` + the companion binary; then `install.sh` installs the agent (`--link`),
  the app, and pairs it (`~/.config/gamecore-studio/app-key.json`). Today it prints these steps as skipped.
- 04 §8.8 items not in this packet's verify: hello through the proxy with the app key (needs P0.5), one designer
  task returning an empty change set (needs P0.5's worker instructions and a working `default` model).
- Prices: no `[models.sla] cost` / `[providers.cost]` are configured (no verified price list), so USD budgets do
  not bind chat models and `max_cost_usd` estimates are 0.
- Pre-existing etos issues not fixed (outside this packet, not needed by its gates): `sdk/rust` clippy
  `manual_is_multiple_of` in `src/realtime.rs:364` and `tests/schema.rs` failing (`schema types the SDK does not
  implement: [ActorDeliveryOutcome, …, RealtimeEvent, …]`) at `6c2c3f4`. The brief's secret-pattern grep, run over
  the whole etos repo, matches pre-existing test fixtures (the fake key file of `crates/etops/tests/support`) and
  an effect key in the doc example of `crates/etops/src/lib.rs`; this branch adds no match (checked on `git diff 6c2c3f4`).

## 5. Operating the node

All on the host (`ssh myubuntu`); `export ETOS_ROOT=~/.local/share/etos-studio PATH=~/.local/opt/etos/bin:$PATH`
for the CLI.

| Action | Command |
|---|---|
| status | `systemctl --user status etosd.service`; `etos node status`; `etos worker list`; `etos models` |
| logs | `journalctl --user -u etosd.service -f` |
| stop / start / restart | `systemctl --user stop|start|restart etosd.service` |
| full bring-up (idempotent) | Mac: `studio/tools/host-sync-etos.sh` then `studio/tools/host-sync-studio.sh push`; host, in `~/wkspace/gc-studio/p0.1-host-etos`: `studio/tools/host-build-etos.sh` then `studio/etos/install.sh` (a second run prints `install.sh: nothing changed (already installed)`) |
| verify | host: `GC_REV=<sha> studio/etos/verify.sh`; Mac: `studio/tools/host-sync-studio.sh pull` to bring the evidence back |
| reset the node | `systemctl --user disable --now etosd.service && rm -rf ~/.local/share/etos-studio` (workers, keys, store and tasks go with it), then `studio/etos/install.sh` re-creates everything; `~/.config/gamecore-studio/providers.env` is regenerated from `~/.bashrc` |
| rotate provider keys | edit the exports in `~/.bashrc`, run `studio/tools/host-providers-env.sh` (or `install.sh`, which restarts etosd when the file changed) |
