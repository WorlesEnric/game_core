# etos W0 verification, 2026-10-04

Produced by `studio/etos/verify.sh` on worlesenric at 20261004T233228Z (UTC). The transcript is
[transcript.txt](transcript.txt) (agent keys and signed URL parameters redacted; no provider key is ever read by this script).

| Item | Value |
|---|---|
| Host | worlesenric, Linux 7.0.0-31-generic, user worlesenric |
| etos | 278ef9cf421f5e64e83a402960def0c51e3833c3 (branch studio/bailian-tts), binaries per `studio/etos/etos.lock` |
| game_core | dd0b05cd16f42a793d9b6d090cdd1cf5e6578665 (HEAD -> worktree-agent-a397841371142d705, origin/worktree-agent-a397841371142d705) |
| Node | root `/home/worlesenric/.local/share/etos-studio`, unit `etosd.service` (user), API `127.0.0.1:7410`, broker `172.17.0.1:7411` |
| Key used | throwaway agent `verify` (grants ops, realtime, files; providers studio-voice), uninstalled with --purge at the end |

| Check | Outcome | Detail |
|---|---|---|
| service | ok | etosd.service active |
| node_status | ok |  |
| verify_agent | ok | key file /home/worlesenric/.local/share/etos-studio/agents/verify/key (mode 600) |
| generate.image | ok | ref ref_01m44m6j35ycg4m99bt8bd9yff, 2416601 bytes |
| describe | ok | A roofed stone well with a wooden crank and hanging bucket stands at the center of a wet cobblestone square in a misty m |
| tts | ok | 73004 bytes; RIFF (little-endian) data, WAVE audio, Microsoft PCM, 16 bit, mono 24000 Hz |
| realtime | ok | {"summary": {"events": ["ready", "closed"], "sent_chunks": 30, "ok": true}} |
| generate.3d | blocked (expected) | not_configured |
| verify_agent_removed | ok |  |
| hello | ok | {"app":"gamecore-unity","capabilities":["requests","candidates","artifacts","index","ops","events","voice","stage"],"connected":true,"node":"studio","protocol":1,"providers":{"3d":"not_configured","de |
| real_node | ok | test result: ok. 2 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 102.92s outcomes: candidate candidate |

Artifacts: `image.png` (generate.image), `describe.json`, `welcome.wav` (tts), `realtime.jsonl`, `hello.json`, `real-node.txt`
(one line per realtime event, audio replaced by its length), `3d.refusal.json`, and the job answers `*.job.json`.
