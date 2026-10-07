# 05. Modality operations and voice

All media come from ETOS ops behind a priced tariff and a per-call ceiling; the Studio imports only bytes whose digest the node reported, through a journaled one-op `asset.import` change set, and never calls a provider itself.

| Family | Provider on the node | Tariff | State today |
| --- | --- | --- | --- |
| `generate.image` | `echo-images`: OpenAI-images kind, `gpt-image-2`, quality low, up to 4 outputs | operator, USD 0.20 per image | live |
| `tts` | `bailian-tts`: DashScope `qwen3-tts-flash`, voice Cherry | published, USD 0.0000114682 per character (Alibaba price page) | live |
| `describe` | `echo-describe`: chat kind on `echo/gpt-5.6-sol` | operator, USD 0.01 per call | live, capped locally because the op has no ceiling field |
| `generate.3d` | none | none | `not_configured` (SADR-020); a Replicate-style predictions example is commented in the template |
| realtime | `studio-voice`: `bailian-omni`, `qwen3-omni-flash-realtime`, semantic VAD | per node | live |

## Request path

Unity's media generator posts `POST /v1/ops/generate` with `max_cost_usd` always set (default 0.50 from `UserSettings/GameCoreStudio.json`). The companion first runs the free `status` op, so `not_configured` is reported before any budget diagnostic; it then prices the call: `budget_unpriced` when no verified tariff exists or the model, parameters or references deviate from the priced entry, `over_budget` when the estimate exceeds the cap. The idempotency key is `gc-<sha256(owner, change set, op, spec, cap)[:40]>` and the charge is recorded once under it. Outputs are fetched through `GET /files/{ref}`, digest-checked, and offered to the Editor; the import policy admits only `.png/.jpg/.jpeg`, `.wav/.ogg/.mp3`, `.fbx` and `.json/.txt/.csv` under `Assets/`, never under `Editor/` or `Plugins/`, never through symlinks, and only with importer settings from a fixed allowlist.

## Tariff provenance

Prices live in `studio/etos/ops.toml.tmpl` with `# @studio` annotations because the pinned ETOS cost config rejects unknown fields; `install-state.py tariffs` validates them (positive `per_unit`; `operator` needs a non-placeholder note; `published` needs an https URL) and `apply_prices` writes them into both the node's `ops.toml` and the companion's `[[ops_prices]]`.

## Voice path

The Editor captures the microphone, down-mixes and resamples to 24 kHz mono PCM16 in 100 ms frames; frames of at most 24 KiB carry a gapless `seq` (a gap is `audio_gap`). The companion bridges `WS /v1/voice` to the node's realtime session for `studio-voice`, forwards only `role == "user"` audio, marks `final: true` only on `done`, and never issues `response_create`, so the provider transcribes and nothing speaks back. Transcripts arrive as `voice_transcript` events; the prompt bar fills with the final text and nothing is sent until the creator presses Send (Ctrl+Enter). Partial revisions are not required (SADR-055): the provider returns one final revision for short takes.

Sources: `studio/etos/ops.toml.tmpl:10-82`, `studio/etos/models.toml.tmpl:56-71`, `studio/etos/install-state.py:83-161`, `studio/agent/src/ops.rs:53-61,201-221,317-397`, `studio/agent/src/pricing.rs:32-129`, `studio/agent/src/voice.rs:1-42,395-407`, `Packages/com.gamecore.studio.etos/Editor/EtosMediaGenerator.cs`, `Editor/PcmSources.cs`, `Client/VoiceFraming.cs:18-21`, `Editor/EtosVoiceSession.cs`, `Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/MediaImportPolicy.cs:23-78`, `Packages/com.gamecore.studio.ui/Editor/Prompt/PromptBar.cs:278-297`.
