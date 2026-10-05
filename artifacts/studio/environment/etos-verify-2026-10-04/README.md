# etos W0 verification, 2026-10-04

Produced by `studio/etos/verify.sh` on worlesenric at 20261004T205506Z (UTC). The transcript is
[transcript.txt](transcript.txt) (agent keys and signed URL parameters redacted; no provider key is ever read by this script).

| Item | Value |
|---|---|
| Host | worlesenric, Linux 7.0.0-31-generic, user worlesenric |
| etos | 5fa113be2e4b8429983d035e212fdb0630e211fa (branch studio/bailian-tts), binaries per `studio/etos/etos.lock` |
| game_core | 22e0cc93febdff48e68ff54ac62f42ff9c85d2b9 (HEAD -> worktree-agent-a397841371142d705, origin/worktree-agent-a397841371142d705) |
| Node | root `/home/worlesenric/.local/share/etos-studio`, unit `etosd.service` (user), API `127.0.0.1:7410`, broker `172.17.0.1:7411` |
| Key used | throwaway agent `verify` (grants ops, realtime, files; providers studio-voice), uninstalled with --purge at the end |

| Check | Outcome | Detail |
|---|---|---|
| service | ok | etosd.service active |
| node_status | ok |  |
| verify_agent | ok | key file /home/worlesenric/.local/share/etos-studio/agents/verify/key (mode 600) |
| generate.image | ok | ref ref_01m44b7jg0t7pjne89kwvmybbj, 2338702 bytes |
| describe | ok | A moss-covered stone well with a wooden frame, shingled roof, rope spindle, and hanging bucket stands in the center of a |
| tts | ok | 80684 bytes; RIFF (little-endian) data, WAVE audio, Microsoft PCM, 16 bit, mono 24000 Hz |
| realtime | ok | {"summary": {"events": ["ready", "closed"], "sent_chunks": 30, "ok": true}} |
| generate.3d | blocked (expected) | not_configured |
| verify_agent_removed | ok |  |
| hello | ok | {"service":"gamecore-studio","version":"0.1.0","protocol":1,"app":"gamecore-unity","node":"studio","sdk":"1.0.0","connected":true,"capabilities":["requests","candidates","artifacts","index","ops","eve |
| real_node | FAILED | thread 'real_node_restart_recovery' (2524867) panicked at tests/real_node.rs:126:13: test result: FAILED. 0 passed; 2 failed; 0 ignored; 0 measured; 0 filtered out; finished in 484.43s  |

Artifacts: `image.png` (generate.image), `describe.json`, `welcome.wav` (tts), `realtime.jsonl`, `hello.json`, `real-node.txt`
(one line per realtime event, audio replaced by its length), `3d.refusal.json`, and the job answers `*.job.json`.
