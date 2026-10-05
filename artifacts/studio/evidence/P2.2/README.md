# P2.2 studio-etos-client - live evidence

Every file here was produced on the Studio host (myubuntu) by `studio/tools/live-etos-tests.sh p2.2`, against the
real etos node (`etosd`, root `~/.local/share/etos-studio`) and the `gamecore-studio` companion, with the paired
`gamecore-unity` app key read from `~/.config/gamecore-studio/app-key.json` (path only; the key is never printed).
Each JSON file was redacted (`EtosRedaction`: `etk_/ett_/etp_/eta_` tokens, bearer values, `etos_ticket=`) and
checked against the key before it was written; the script re-scans every file afterwards and fails on a hit.

| Folder | Run |
|---|---|
| `live-20261005T002957Z/` | dotnet live tests (`GameCore.Studio.Etos.Client.Tests`, Category `Live`), 5/5 passed |

Files of a run:

- `dotnet-*.json` - one evidence document per row from the dotnet client (`LiveTests.cs`);
  `dotnet-exchanges-<test>.json` - every HTTP exchange of the test (method, companion path, status, ms, etos code)
- `unity-*.json` - one evidence document per row from the Editor (`EtosLiveTests.cs`)
- `fixtures/` - sanitized answers recorded for the replay fixtures (copied to
  `dotnet/tests/GameCore.Studio.Etos.Client.Tests/Fixtures/`)
- `voice-prompt.wav` - the spoken prompt ("Move this NPC two metres north.") produced by the node's TTS provider and
  streamed into the realtime voice session
- `dotnet-live.log`, `dotnet-live.trx`, `unity-live.log`, `unity-live-results.xml` - runner output
