# Provider probe, 2026-10-04 (environment baseline)

Probed from the build host `myubuntu` (Linux 7.0, x86_64) between 17:40 and 18:30 UTC by the
P0.1 owner, with `curl` and with etos itself. Keys were loaded into the probing shell from the
host's `~/.bashrc` exports (`eval "$(grep -E '^export [A-Z_]+_API_KEY[=]' ~/.bashrc)"`) and were
never printed, copied or written to a file other than etosd's 0600 environment file. Bodies below
are quoted verbatim where a refusal matters; signed URLs are shortened.

## Echo, `https://api.echo-coding.com/v1` (OpenAI-compatible; `ECHO_API_KEY`)

| Route / model | Result |
|---|---|
| `GET /models` | 200; lists, among others, `claude-opus-5-5`, `claude-sonnet-5-5`, `gpt-6-sol`, `gpt-5.6-sol`, `gpt-image-2`, `gpt-image-1.5` |
| `POST /chat/completions` `gpt-6-sol` | 200, "OK." (3.7 s) |
| `POST /chat/completions` `gpt-5.6-sol` | 200, "OK" (5.0 s); also answered etos `describe` with an image (verify transcript) |
| `POST /chat/completions` `claude-opus-5-5` | **refused** at 17:58 UTC: HTTP 401 `{"type":"error","error":{"type":"authentication_error","message":"OAuth access token has been revoked."},"request_id":null}`; at 18:05 UTC: HTTP 503 `{"error":{"message":"auth_unavailable: no auth available (providers=claude, model=claude-opus-5-5); check Claude auth/key session and cooldown state via /v0/management/auth-files", …` |
| `claude-sonnet-5-5`, `claude-fable-5-1` | HTTP 401, same `OAuth access token has been revoked.` body; `claude-opus-5-5-high`: 404 `Requested entity was not found.` |
| `POST /images/generations` `gpt-image-2`, `quality=low`, `size=1024x1024` | 200 with `response_format=b64_json` (19.1 s) and without it (17.2 s): one b64 PNG of ~1.2 MB, `size` reported `1374x1145` / `1360x1157` (the provider picks the size), usage 10 input + 429 output tokens |
| `POST /audio/speech`, `POST /audio/transcriptions` | 404 (no audio routes) |

Consequence: the catalog's `default` alias (`echo/claude-opus-5-5`, the Studio workers' model) is
configured as specified but **was not answering** at probe time; it is an Echo-side
credential problem, not an etos one. `fast` (`gpt-6-sol`) and `describe` (`gpt-5.6-sol`) work.

## DashScope / Bailian (`BAILIAN_API_KEY`)

| Route / model | Result |
|---|---|
| `POST https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation` `{"model":"qwen3-tts-flash","input":{"text":"Hello.","voice":"Cherry","language_type":"English"}}` | 200: `{"output":{"audio":{"data":"","expires_at":…,"id":"audio_…","url":"http://dashscope-result-bj.oss-cn-beijing.aliyuncs.com/prod/qwen3-tts/…/….wav?Expires=…&OSSAccessKeyId=…&Signature=…"},"finish_reason":"stop"},"usage":{"characters":6},"request_id":"…"}`. The URL is **http**, signed, on an `aliyuncs.com` object-store host, valid about 24 h |
| same with `"parameters":{"format":"mp3"}` | 200, still a `.wav` URL (the parameter is ignored): the native route produces WAV only |
| same with `"voice":"NoSuchVoice"` | HTTP 400 `{"request_id":"…","code":"InvalidParameter","message":"Invalid voice specified, the requested voice does not exist or is not licensed for use—please select a supported voice."}` |
| `POST /compatible-mode/v1/audio/speech`, `/audio/transcriptions`, `/images/generations` | 404 |
| realtime `qwen3-omni-flash-realtime` (`wss://dashscope.aliyuncs.com/api-ws/v1/realtime`) | through etos `/realtime/connect?provider=studio-voice`: `ready` (capabilities `duplex_audio, manual_response, context_replace, response_cancel, playback_truncate, speech_detection, input_transcript`, pcm16 24 kHz) and a clean `closed` after 3 s of input (2 s 440 Hz tone + 1 s silence); a pure tone produces no speech events |

Network note: `dashscope.aliyuncs.com` resolves to IPv4 and IPv6 addresses; IPv6 connections fail
at once from this host (`curl -6`: "Couldn't connect to server" after 0 ms), IPv4 works directly,
and the desktop proxy `127.0.0.1:7897` also works. etosd's unit unsets the desktop proxy
variables, so etos reaches both providers directly (Echo over IPv6 or IPv4, DashScope over IPv4).
One etos `tts` call right after the node's first start failed with `provider_unavailable: cannot
connect`; every later call succeeded (see the verify transcripts).

## Not available

| Capability | Status |
|---|---|
| 3D generation (`family = "3d"`, `predictions`) | **blocked**: no provider credential on the host (SADR-020); etos answers `not_configured` |
| Speech-to-text by file | not configured (Echo and DashScope OpenAI-compatible transcription routes are 404); voice input is realtime |
| DeepSeek (`DEEPSEEK_API_KEY`) | key present in providers.env, not used by any Studio configuration |
