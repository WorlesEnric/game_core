# PACKET P2.2 studio-etos-client

Owner: Opus 5.5. Contract: [docs/studio/04-etos-integration.md](docs/studio/04-etos-integration.md) s2-s5, s8;
02 s2 (boundary D), s4 (request path), 03 s2/s3/s6/s9; companion API of P0.5 (`studio/agent/src/{api,model,desk,
voice,ops}.rs`, P0.5 note s4). Branch `worktree-agent-ade08c7cbb82eacb1`, based on `main` `5beddd1` (P1.6 merged).

## Built

| Path | What |
|---|---|
| `Packages/com.gamecore.studio.etos/Client/**` | asmdef `GameCore.Studio.Etos.Client` (Editor-only, `noEngineReferences`, no references, Newtonsoft only): `CompanionClient` (every companion route: hello, requests POST/GET/list/cancel, candidates, artifacts streamed to a temp file and verified by sha256 + size before use, index delta, ops/generate with `max_cost_usd` always sent (default 0.50), stage + stage job, tickets, ticketed WebSockets, an authority probe), `EventStream` (ticket → `/v1/events?after=N`, gapless resume, exponential backoff with jitter, persisted cursor), `VoiceChannel` + `VoiceFraming` (24 kHz mono PCM16, frames ≤ 24 KiB, gapless seq, stop/closed), `EtosErrors` (etos-shaped errors with the code preserved; client codes `transport`, `protocol`, `timeout`, `artifact_digest_mismatch`, `artifact_size_mismatch`), `EtosRedaction`, `EtosCredentials` (key file only), `CursorStore`, `BackoffPolicy`, lenient DTOs |
| `Packages/com.gamecore.studio.etos/Editor/**` | asmdef `GameCore.Studio.Etos` (Editor): `EtosAgentGateway` (implements both gateway interfaces), `AgentRequestBuilder`, `MainThreadQueue`, `EtosStudioSession` (ScriptableSingleton) + `EtosBootstrap`, `EtosSettings` + `EtosSettingsProvider` ("Project/GameCore Studio/ETOS"), `RedactingStudioLog`, `EtosMediaGenerator` (media adapter), `MicrophoneCapture` / `WavPcmSource` (`IPcmSource`), `EtosVoiceSession` |
| `Packages/com.gamecore.studio.etos/Tests/Fake/**` | asmdef `GameCore.Studio.Etos.Testing` (`UNITY_INCLUDE_TESTS`, Unity-free): `FakeCompanion` over a socket-level `MiniHttpServer` (HTTP/1.1 + RFC 6455 server frames; same code under Mono and .NET 8) |
| `Packages/com.gamecore.studio.core/Runtime/Authoring/Agent/AgentContracts.cs` | the shared names of the brief in namespace `GameCore.Studio.Authoring.Agent`: `IAgentGateway`, `IVoiceSession`, `ProviderStatus`/`ProviderState`, `AgentRequest`, `Attachment`, `RequestView`, `CandidateNotice`, `OpRequest`, `OpResult`, `TranscriptUpdate`, plus `AgentGatewayLookup.From(...)` |
| `dotnet/src/GameCore.Studio.Etos.Client/` | netstandard2.1 project compiling the Client sources (added to `dotnet/GameCore.sln`) |
| `dotnet/tests/GameCore.Studio.Etos.Client.Tests/` | NUnit tests (net8.0; added to the sln): routes, events, redaction, voice, fixture replay, live |
| `dotnet/tests/.../Fixtures/*.json` | answers recorded from the real node (redacted) |
| `games/hollowmere/Packages/manifest.json` (+ lock) | `com.gamecore.studio.etos` added (file: package) and to `testables` |
| `games/hollowmere/Assets/Hollowmere/Tests/P2_2/**` | asmdef `GameCore.Studio.Hollowmere.P2_2.Tests`: `EtosGatewayTests` (fake), `EtosLiveTests` (live, env-gated), `GatewayHarness` |
| `studio/tools/live-etos-tests.sh` | host live verification (dotnet + Unity live tests, PipeWire virtual mic, redaction scan) |
| `artifacts/studio/evidence/P2.2/**` | redacted live evidence |
