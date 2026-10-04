# PACKET P0.1 host-etos

Owner: Opus 5.5. game_core branch `worktree-agent-a397841371142d705` (also on the host hub
`myubuntu:~/wkspace/gc-studio/hub.git`). etos branch `studio/bailian-tts` in
`/Users/yangcao/wkspace/etos` (also on `myubuntu:~/wkspace/gc-studio/etos-hub.git`; not pushed
anywhere else). Design: `docs/studio/04-etos-integration.md` §5, §8; `02-architecture.md`
SADR-002/005/018/019/020.

**Status in one line:** the node, providers, images, workers, companion agent and Unity app are
installed and `install.sh` is idempotent; `verify.sh` passes every etos check (image, describe,
TTS, realtime, 3D refusal, hello through the proxy) but **fails the companion's real-node test**,
because Echo's `claude-opus-5-5` (the workers' `default` model) is down and a task launched through
`POST /tasks` can stay `starting` (§3, `artifacts/studio/environment/etos-verify-2026-10-04/DIAGNOSIS.md`).

## 1. Built

### etos (`studio/bailian-tts`, base `6c2c3f4`, head `5fa113b`)

| Commit | What |
|---|---|
| `10e2ae0` | etops: `bailian.rs` within the workspace lints (15 `Mutex::lock().unwrap()` outside tests → `unwrap_or_else(into_inner)`, rustfmt). **`scripts/check.sh rust` failed at `6c2c3f4` itself** (clippy `unwrap_used`, fmt), so the gate required this. |
| `6f30728` | etops: provider kind **`bailian-tts`** (SADR-005 a). New `src/speech_bailian.rs`; `config.rs` (kind, serves `tts`, default base URL `https://dashscope.aliyuncs.com/api/v1`, needs `model`); `service.rs` (build); `lib.rs` (export). POST `services/aigc/multimodal-generation/generation` `{model, input:{text, voice, language_type?, instructions?}}`; `output.audio.data` (base64) if present, else downloads `output.audio.url` only from the provider's host or a `download_hosts` suffix (default `aliyuncs.com`; http/https only; credential never sent there); usage from `usage.characters`; WAV only (`format` other than wav and `speed` refused `bad_args` before sending); HTTP 4xx → `request_rejected`, 429/5xx → `provider_unavailable` (billed create, not retried), 200 with error `code` → `job_failed`, disallowed URL → `bad_response`. Tests: `tests/speech_bailian.rs` (own imitation of DashScope: success and not repeated, inline audio, 400, 429, error code, host refusal without download, unsupported format) and unit tests (body, URL rule, config). README: provider table, a `bailian-tts` section listing its differences from `openai-speech`, Tests. |
| `f3eda1a` | sdk/rust: `Ops::generate(family, input)` posts `generate.<family>` (it posted `generate`, which the node answers `unknown_operation`); bad family (`""`, `.`, `/`) → `Error::Invalid`. Test in `tests/agent.rs`; `sdk/rust/README.md`. API change: the method gains `family` (no callers existed). |
| `3e8b336` | etops `files.rs`: media type of an extensionless input from its signature bytes. **Outside the listed etos paths; a verification row proved the gap** (SADR-005 b): agent ops pass pinned references as store paths without extension, so `describe` (also `ocr`, `transcribe`, generation `references`) always refused `unsupported_input`. Unit test, README sentence. |
| `5fa113b` | etrg `actors/mod.rs`, `types.rs`: worker names checked with the node's rule in `open_view`. **Outside the listed paths; found by the companion's real-node test**: every task on a worker with `-` in its name (`gc-designer`) was refused at open with `bad_name`. Unit test. |

Each of the last two is a separate commit so it can be taken or dropped on its own.

`scripts/check.sh rust` (fmt, clippy -D warnings, tests, doc, boundaries, cargo-deny, budgets) runs on
the host in `~/wkspace/etos-studio`: @GATES@

### Host (`myubuntu`, user `worlesenric`)

| Item | Path / value |
|---|---|
| etos source | `~/wkspace/etos-studio` = git clone of `etos-hub.git` at `5fa113b` (`studio/tools/host-sync-etos.sh`). `~/wkspace/etos` (unrelated) untouched. |
| binaries | `~/.local/opt/etos/bin/{etosd,etos}` (glibc) and `etos-musl` (static-pie, also `<root>/bin/etos`); sha256 in `studio/etos/etos.lock` (`commit = "5fa113b…"`, layer version 3) |
| node root | **`~/.local/share/etos-studio`** (`etosd init --name studio --owner worlesenric`; not `--profile local`). Not `~/.local/share/etos` as briefed: that directory already holds an unrelated etos installation on this host (`personal/`, `personal-backend/`, `backups/`, served by the running user unit `etos-node.service`). `ETOS_STUDIO_ROOT` overrides. |
| config | `<root>/etos.toml`, `models.toml`, `ops.toml` from `studio/etos/*.tmpl` (only `@NODE_NAME@`, `@OWNER@` substituted; no secrets) |
| ports | SDK API `127.0.0.1:7410`; broker listen + authority `172.17.0.1:7411`; web UI `127.0.0.1:7400` |
| provider keys | `~/.config/gamecore-studio/providers.env` (0600; ECHO, BAILIAN, DEEPSEEK keys), written on the host from `~/.bashrc` by `studio/tools/host-providers-env.sh`, never printed |
| service | user unit `~/.config/systemd/user/etosd.service`: `EnvironmentFile=` the file above, `ExecStart=~/.local/opt/etos/bin/etosd run --root <root>`, desktop proxy variables unset (both providers answer directly over IPv4); enabled; linger on |
| images | `localhost/etos-default:latest` `sha256:7b15d913…`; `localhost/gc-designer:current` `sha256:46280666…` (Debian trixie-slim: bash, coreutils, git, tmux, python3, jq, ImageMagick 7, ffmpeg 7); `localhost/gc-mechanic:current` `sha256:fb567ff0…` (adds .NET SDK 8.0.425 from `mcr.microsoft.com/dotnet/sdk:8.0@sha256:78235e09…`, NuGet cache `/opt/nuget/packages` pre-restored for a netstandard2.1 library and an NUnit test project). Built with `--provenance=false` so unchanged builds keep their id. |
| workers | `gc-designer`, `gc-mechanic`: `etos worker create <w> --image localhost/<w>:current --network offline --model default --instructions …`, created before the agent (its install keeps image and network) |
| models | `echo/claude-opus-5-5` = `default`, `echo/gpt-6-sol` = `fast`, `echo/gpt-5.6-sol` = `describe` |
| ops | image `echo-images` (`gpt-image-2`, `{quality="low", response_format="b64_json"}`), describe `echo-describe` (chat, `echo/gpt-5.6-sol`), tts `bailian-tts` (`qwen3-tts-flash`, Cherry, English), realtime `studio-voice` (`bailian-omni`, `qwen3-omni-flash-realtime`, `semantic_vad`, 0.5); 3d absent (documented in `ops.toml.tmpl`) |
| agent | `gamecore-studio` installed `--link` from `~/wkspace/gc-studio/p0.1-host-etos/studio/etos/agent` (`bin/gamecore-studio` → `../../../agent/target/release/gamecore-studio`, built by install.sh), state `ready` |
| app | `gamecore-unity` installed (`studio/etos/app/app.toml`), paired: `~/.config/gamecore-studio/app-key.json` (0600) |
| game_core on the host | `~/wkspace/gc-studio/p0.1-host-etos` = git clone of `hub.git`, this branch |

Other host changes: `cargo-deny` installed in `~/.cargo/bin`; `rust:1.97.1-alpine` pulled; musl
cargo registry cache `~/.cache/gamecore-studio/cargo-musl`; the former rsync copies were renamed
`~/wkspace/etos-studio.rsync-old` and `~/wkspace/gc-studio/p0.1-host-etos.rsync-old` (their
`target/` dirs moved into the clones; safe to delete).

### game_core files (this branch)

`studio/etos/{etos.toml.tmpl, models.toml.tmpl, ops.toml.tmpl, etosd.service.tmpl, install.sh,
verify.sh, etos.lock, app/app.toml, verify-agent/{agent.toml, realtime_probe.py}}`,
`studio/images/{gc-designer/Dockerfile, gc-mechanic/Dockerfile, gc-mechanic/prewarm/**}`,
`studio/tools/{host-sync-etos.sh, host-sync-studio.sh, host-build-etos.sh, host-build-images.sh,
host-providers-env.sh}`, `artifacts/studio/environment/{provider-probe-2026-10-04.md,
etos-verify-2026-10-04/**}`, this file. No file outside these paths was changed (main was merged in
for P0.5's agent).

## 2. Verified (how)

All on the host; the Mac only edits and runs git.

- **etos gates**: `scripts/check.sh rust` in `~/wkspace/etos-studio` (see §1). The `bailian-tts` tests and the new unit
  tests ran there too.
- **Idempotency**: `studio/etos/install.sh` run twice at the final pin (`/tmp/gc-p01-install{1,2}.log` on the host).
  Run 1: `install.sh: 2 change(s) applied` (etosd restarted for the new binary; gc-mechanic image rebuilt after the
  switch to a git clone). Run 2: `install.sh: nothing changed (already installed)`. `host-build-images.sh` twice:
  all `unchanged`. `host-build-etos.sh` at `5fa113b`: `etos` and `etos-musl` `unchanged` (the etrg fix only
  changes `etosd`, which was replaced).
- **verify.sh** (`artifacts/studio/environment/etos-verify-2026-10-04/`, run 20:55 UTC at game_core `22e0cc9`,
  etos `5fa113b`):
  - service ok; `etos node status` ok.
  - `generate.image` ok (2.3 MB PNG, `image.png`).
  - `describe` ok ("A moss-covered stone well…").
  - `tts` ok (`welcome.wav`, WAV PCM16 mono 24 kHz, 80 684 bytes).
  - realtime ok (`ready` then a clean `closed`, 30 chunks: 2 s of 440 Hz and 1 s of silence).
  - `generate.3d` blocked as expected (`not_configured`).
  - verify agent uninstalled with `--purge`.
  - `GET …/agents/gamecore-studio/http/v1/hello` with the app key → 200.
  - **`real_node` FAILED** (0/2, both "did not settle"). `verify.sh` exits 1 for that reason.
- **Worker containers**: after an etosd restart the stuck designer task was relaunched and ran in
  `etos-gc-designer-t658…` on the node's layer over `gc-designer:current` (DIAGNOSIS.md item 3).
- **Providers**: `artifacts/studio/environment/provider-probe-2026-10-04.md` (direct calls; what works, what is 404,
  what is blocked).
- **Secrets**: the brief's secret-pattern grep over `studio/etos`, `studio/images`, `studio/tools`,
  `artifacts/studio` and this file finds nothing. The only matches in the repo are P0.5's test fixtures in
  `studio/agent`. In etos, `git diff 6c2c3f4` adds no match.

## 3. Refusals, failures and deviations (verbatim)

- **real_node** (verify item 9): `cs_… did not settle: {…"state":"running","taskId":"t658160503cb112b318184111","taskStatus":"starting"…}`
  and, for the restart scenario, `"taskStatus":"queued"`; `test result: FAILED. 0 passed; 2 failed`. Causes
  (DIAGNOSIS.md):
  - (a) The task stayed `starting`, with no image build or container, until etosd was restarted. The likely
    cause is that `POST /tasks` launches the task inline and the handler was dropped when the SDK's 30 s request
    timeout expired under heavy host load.
  - (b) Once the task ran, its turn failed on the model: `HTTP 503: auth_unavailable: no auth
    available (providers=claude, model=claude-opus-5-5); …`, then `model marked degraded model=echo/claude-opus-5-5`.
- **Earlier real_node "passes" were false**: before etos `5fa113b` the test reported 2/2 passed while every request
  ended `failed` at `phase: open` with ``"`gc-designer` is not a valid worker name; use a lowercase letter, then
  lowercase letters, digits or _ (at most 64)"``. P0.5's test should not accept that outcome.
- Echo `claude-opus-5-5`: HTTP 401 `OAuth access token has been revoked.`, later HTTP 503 `auth_unavailable…`
  (provider probe). Configured as specified, not substituted.
- `etos worker create gc-allowlist-probe --image localhost/gc-designer:current --network allowlist: --model default`
  → `error: invalid network policy `allowlist:`: use `open`, `allowlist:<host>,<host>` or `offline`` (etos
  `crates/etbroker/src/db.rs`). The workers therefore use `--network offline` (no egress at all). P0.5's
  `agent.toml` comment and 04 §1/§4 say `allowlist:`, which etos cannot express.
- First verify run (before `3e8b336`): `describe` → `{"code":"unsupported_input","message":"image description cannot
  read `…/store/sha256/2c/4351…`: this provider accepts PNG, JPEG, GIF or WebP images (and PDFs for ocr)"}`.
- First verify run: one `tts` → `provider_unavailable: cannot connect (error sending request for url
  (https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation))` right after the node's
  first start. It succeeded a minute later and in every later run; not reproduced.
- `generate.3d` → HTTP 503 `{"code":"not_configured","message":"3D model generation is not available on this node:
  no provider is configured", …}` (expected, SADR-020).
- `studio/etos/app/app.toml` is listed under P0.5's paths in 06 §2. It was written here as briefed, with the
  content from the coordinator's note.

## 4. Remaining

- A designer or mechanic task cannot complete until Echo serves `claude-opus-5-5` again, or the owner picks another
  `default`. The real-node test and 04 §8.8's "designer task returns an empty change set" both depend on it.
- The `starting` stall needs a decision: launch tasks detached from the `POST /tasks` request in etos, or give that
  call a longer timeout in the companion.
- No prices are configured (`[models.sla] cost`, `[providers.cost]`), so USD budgets do not bind and
  `max_cost_usd` estimates 0.
- Pre-existing etos issues outside this packet's gates, present at `6c2c3f4`: `sdk/rust` clippy
  `manual_is_multiple_of` (`src/realtime.rs:364`) and a failing `tests/schema.rs` (schema types the SDK does not
  implement, e.g. `RealtimeEvent`).
- `studio/agent/vendor-etos-sdk.sh` can now be run against `5fa113b` (the lock's `commit =` line); that commit
  contains the `sdk/rust` change (`generate(family, …)`).

## 5. Operating the node

On the host: `export ETOS_ROOT=~/.local/share/etos-studio PATH=~/.local/opt/etos/bin:$PATH`.

| Action | Command |
|---|---|
| status | `systemctl --user status etosd.service`; `etos node status`; `etos worker list`; `etos agent list`; `etos models` |
| logs | `journalctl --user -u etosd.service -f`; `etos agent logs gamecore-studio` |
| stop / start / restart | `systemctl --user stop\|start\|restart etosd.service` |
| bring-up (idempotent) | Mac: commit, then `studio/tools/host-sync-etos.sh` and `studio/tools/host-sync-studio.sh push`. Host, in `~/wkspace/gc-studio/p0.1-host-etos`: `studio/tools/host-build-etos.sh`, then `studio/etos/install.sh` (a second run prints `nothing changed`) |
| verify | Host: `studio/etos/verify.sh` (exit 0 only if every check passes). Mac: `studio/tools/host-sync-studio.sh pull` commits the lock and evidence on the host and fast-forwards the Mac branch |
| reset | `systemctl --user disable --now etosd.service && rm -rf ~/.local/share/etos-studio ~/.config/gamecore-studio/app-key.json`, then `studio/etos/install.sh` re-creates the node, workers, agent, app and key (`providers.env` is regenerated from `~/.bashrc`) |
| rotate provider keys | Edit the exports in `~/.bashrc`; `studio/etos/install.sh` rewrites `providers.env` and restarts etosd |
