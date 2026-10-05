# Provider probe, 2026-10-05: worker model switch

Probed from `myubuntu` at 2026-10-04T22:45Z (06:45 on 2026-10-05, host local time) by the P0.1
owner. The keys were loaded from the host's `~/.bashrc` exports into the probing shell only and
were never printed or written anywhere. Each request was `POST <base>/chat/completions` with
`{"model": …, "max_tokens": 20, "messages": [{"role": "user", "content": "Reply with OK."}]}`.

| Provider, model | Result (verbatim) |
|---|---|
| Echo `https://api.echo-coding.com/v1`, `claude-opus-5-5` | HTTP 503 (1.2 s) `{"error":{"message":"auth_unavailable: no auth available (providers=claude, model=claude-opus-5-5); check Claude auth/key session and cooldown state via /v0/management/auth-files","type":"server_error","code":"internal_server_error"}}` |
| Echo, `claude-sonnet-5-5` | HTTP 503 (1.2 s), the same body for `model=claude-sonnet-5-5` |
| Echo, `gpt-6-sol` | HTTP 200 (5.8 s) `"OK."` |
| DeepSeek `https://api.deepseek.com`, `deepseek-chat` | HTTP 200 (0.8 s) `"OK"` |

The 2026-10-04 probe ([provider-probe-2026-10-04.md](provider-probe-2026-10-04.md)) and the
coordinator's check from the host on 2026-10-05 got HTTP 401 `OAuth access token has been revoked.`
for the same Claude models. The 503 above is how Echo answers once it has marked that credential
unavailable. It is the same upstream problem on Echo's side, not an etos one. The etos node logged
the same 503 for the designer task in the 2026-10-04 verify run
(`etos-verify-2026-10-04/DIAGNOSIS.md`, item 4).

## Decision (coordinator, 2026-10-05)

The Studio workers `gc-designer` and `gc-mechanic` now use **`echo/gpt-6-sol`** (catalog alias
`fast`), set in `studio/etos/agent/agent.toml`. The preferred `default` model (`echo/claude-opus-5-5`)
stays in the catalog and is kept as a comment beside each worker; switch back when Echo serves it
again. `deepseek-chat` answers too, but no DeepSeek endpoint is configured in `models.toml`.
