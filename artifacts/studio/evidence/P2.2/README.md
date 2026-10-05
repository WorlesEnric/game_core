# P2.2 studio-etos-client - live evidence

Every file here was produced on the Studio host (myubuntu) by `studio/tools/live-etos-tests.sh p2.2`, against the
real etos node (`etosd`, root `~/.local/share/etos-studio`) and the `gamecore-studio` companion, with the paired
`gamecore-unity` app key read from `~/.config/gamecore-studio/app-key.json` (path only; the key is never printed).
Each JSON file was redacted (`EtosRedaction`: `etk_/ett_/etp_/eta_` tokens, bearer values, `etos_ticket=`) and
checked against the key before it was written; the script re-scans every file afterwards and fails on a hit (no
run had a hit).

## Runs (2026-10-05, UTC folder names)

| Folder | dotnet live (5) | Unity live (4) | Notes |
|---|---|---|---|
| `live-20261005T002957Z/` | 5/5 | not run | first live pass of the protocol client; fixtures recorded here |
| `live-20261005T015317Z/` | 5/5 | 3/4 | row b **passed for the wrong reason**: a fresh journal made the gateway import an old candidate from the shared ledger (fixed: only this project's requests are imported); row c failed with the companion's 30 s node-call timeout (`transport`, 502) |
| `live-20261005T020306Z/` | 5/5 | 0/4 | row b: the real worker answered `needs_clarification` (a definition was selected, not the placed NPC); row c: the same effect key as the previous run returned that run's job without an artifact (fixed: per-run change-set id, job state read); voice: the realtime provider refused session setup (`bad_response`, "upstream session.updated was not acknowledged") |
| `live-20261005T021749Z/` | 0/5 (external) | 1/4 | **external event, not a client failure**: the P0.5 owner reinstalled the companion binary mid-run; the client reported `bad_gateway` (502) then `agent_starting` (503) with the codes preserved. Unity: image passed (row c); row b `needs_clarification` (no position in the index slice; fixed: `scene-context.json` attachment); microphone heard silence because the spoken prompt comes from the dotnet voice test that the restart broke |
| `live-20261005T023249Z/` | **5/5** | **3/4** | final run: rows a, b, c, d, f, g, h, i, j and W-ETOS-02 pass; row e (describe) blocked by a companion defect (below) |

## Final rows (run `live-20261005T023249Z`)

| Row | Result | Evidence |
|---|---|---|
| (a) hello through the proxy | pass, 46 ms; providers image/tts/describe/voice live, 3d not_configured | `dotnet-a-hello.json` |
| W-ETOS-02 authority | another agent: 404 `agent_unknown` (only `gamecore-studio` is installed); agent-only route `/api/v1/tasks/...`: 403 `forbidden` | `dotnet-w-etos-02-authority.json` |
| (b) "Move this NPC two metres north." to staging | pass: the placed Traveller (ThornwickVillage, authoring id `90f935f8-…`) selected; worker `gc-designer` task `tb0fec1af63e78e9a74901cdb` returned one `move` op, position [0, 0, -12] → [0, 0, -10]; imported and staged in Candidate mode with no diagnostics (65 ms), journaled `Candidate`, then rejected (review only) → `Rejected`; 0 foreign candidates staged | `unity-b-npc-request.json` |
| (c) 256×256 "wooden well icon" PNG ≤ 200 KB | pass: `echo-images`, 43 s, provider PNG 1,450,060 B down-sampled in the Studio to 256×256, 85,344 B, sha256 `ba3f86b4…`, applied through a journaled `asset.import` | `unity-c-image.json`; asset `games/hollowmere/Assets/Hollowmere/Generated/P2_2/wooden_well_icon.png` |
| (d) TTS "Welcome to Thornwick" WAV | pass: `bailian-tts`, 11.7 s, 1.44 s audio, 69,164 B, sha256 `6bc7dd7d…` | `unity-d-tts.json`; asset `.../Generated/P2_2/welcome_to_thornwick.wav` |
| (e) describe | **blocked (companion defect)**: 400 `bad_args` "unknown field `max_cost_usd`" | `unity-e-describe.json` |
| (f) 3D refusal | pass: 503 `not_configured` with the operator hint, passed through untouched | `unity-f-3d-refusal.json`, `dotnet-f-i-refusals.json` |
| (g) events resume | pass: forced disconnect, 2 connects, reconnect 257 ms, cursors 122-125 identical to a fresh replay (no gap, no duplicate) | `dotnet-g-h-events-resume-cancel.json` |
| (h) cancel | pass: cancel ack 8.9 s; final `cancelled`, no candidate, one task | same file |
| (i) tampered artifact refused before Put | pass: `artifact_digest_mismatch`, store entries unchanged (Editor); same in the dotnet client | `unity-i-tamper.json`, `dotnet-f-i-refusals.json` |
| (j) voice | pass: spoken WAV via `EtosVoiceSession` ("More this NPC two meters north.", final 5.1 s after start); Editor microphone on the PipeWire virtual source `GC_P2.2_mic` (24 kHz, peak 0.198, 140 frames, same transcript); dotnet `VoiceChannel` (final 3.8 s after start) | `unity-j-voice-wav.json`, `unity-j-voice-mic.json`, `dotnet-j-voice.json` |

Timings are single samples on a loaded host (load average ~35), not benchmarks. The voice session close reason
`client closed` is what etos reports when the companion closes the realtime session after `stop`.

## Companion defect blocking row (e) (not patched: `studio/agent` is outside P2.2)

`studio/agent/src/ops.rs` `generate()` inserts `max_cost_usd` into the op input before it branches to
`describe()`, which forwards that input to the etops `describe` operation; etops' `UnderstandArgs` accepts only
`input, prompt, language, output, provider` and refuses the call. The client cannot avoid it: when the caller omits
`max_cost_usd` the companion inserts its configured default the same way.

Repro (any app-key client):

```
POST /api/v1/agents/gamecore-studio/http/v1/ops/generate
{"op":"describe","spec":{"artifact":"<a stored sha256>"},"max_cost_usd":0.5}
→ 400 {"code":"bad_args","message":"the arguments of `describe` are invalid: unknown field `max_cost_usd`, expected one of `input`, `prompt`, `language`, `output`, `provider`"}
```

Fix belongs to P0.5: remove `max_cost_usd` from the input on the describe path (or before the etops call for short
ops).

## Files of a run

- `dotnet-*.json` - one document per row from the dotnet client (`LiveTests.cs`); `dotnet-exchanges-<test>.json` -
  every HTTP exchange of the test (method, companion path, status, ms, etos code)
- `unity-*.json` - one document per row from the Editor (`EtosLiveTests.cs`); `unity-log-<test>.json` - the
  redacted Studio log of the test
- `fixtures/` - sanitized answers recorded for the replay fixtures (the first run's set is in
  `dotnet/tests/GameCore.Studio.Etos.Client.Tests/Fixtures/`)
- `voice-prompt.wav` - the spoken prompt ("Move this NPC two metres north.") from the node's TTS provider, streamed
  into the realtime session by the dotnet test and played into the virtual microphone for the Editor test
- `dotnet-live.log`, `dotnet-live.trx`, `unity-live.log`, `unity-live-results.xml`, `pw-*.log`, `mic-listening` -
  runner output
