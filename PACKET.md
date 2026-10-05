# R2-D — Studio ETOS client and media integration

Branch `codex/r2-d`, Linux build host `myubuntu`. `git fetch origin && git merge origin/main`
reported already up to date at `de2d9593`. R2-A's former root note is now
`docs/studio/packets/R2-A-core-edit-recovery.md`; its published seams are consumed here.
No installed service, sibling clone, real credential file, paid operation or live-node test is touched.

## R2 fixes

| Finding | Fix | Regression |
|---|---|---|
| R2-13 client / D5 | Four-field stage POST; project header on HTTP, artifact, ticket and WebSocket calls; exact signed record fetch/verify; IStageService and ICandidateStageGateway; persisted productGUID/path identity; local source/catalog callbacks rebound on session startup | `R2_13_StageUsesExactOwnedContractAndVerifiesUnmodifiedRecord`, `R2_13_ProjectIdentityPersistsAndTrustedContextRebinds` |
| R2-20 | Await main-thread handling before cursor save; stop cancels queued work; handler/save failure reconnects from last acknowledged cursor with `event_handler_failed` | `R2_20_UnhandledEventIsReplayedAfterStopAndThrowDoesNotAcknowledge`, `R2_20_MainThreadQueueMustHandleBeforeCursorSaveAndReloadReplays` |
| R2-21 | Socket ownership transfers only on successful connect; finally disposes unsuccessful connections; explicit abort/dispose and cancellable wait handle Mono stalled handshakes; stream shutdown disposes connected sockets | `R2_21_CancelTicketedConnectAndRepeatedReloadLeaveNoConnections`, `R2_21_CancelDuringTicketedConnectDisposesConnection` |
| R2-22/23 client | Core SecretRedactor for strings and nested payloads; no raw InnerException; retain sanitized structured timeout data, HTTP status, provider code and hint | `R2_22_23_TimeoutRetainsRedactedStructuredDataWithoutRawException`, `R2_22_23_DiagnosticRetainsSanitizedStructuredTimeout` |
| R2-24 | 128 inspected objects, 64 KiB UTF-8 envelope, bounded strings/hierarchy, explicit truncation, secret redaction before packing | `R2_24_SceneContextBoundsObjectsBytesAndRedactsNames` |
| R2-41 | Discoverable parameterless IMediaGenerationGateway; asynchronous Requested ID; configured max_cost_usd; verified download plus import-side digest check; journaled asset.import | `R2_41_AudioToolDiscoversGatewayAndImportsVerifiedVoiceThroughEngine`; existing media/import regression |

The standalone dotnet client compiles the actual core `StudioLog.cs` policy, with a console-only
UnityEngine.Debug bridge, rather than copying the policy. Unity references GameCore.Studio.Authoring.
Transport URL/punctuation normalization remains in EtosRedaction before the core policy.
D9 deliberately masks the timeout `data.key` value; its structured field and `data.op` survive.
Recovery resends the identical original body, so it does not require exposing the effect key.

Project ID is SHA-256 of lowercase productGUID + newline + canonical absolute project directory.
It is persisted separately in `UserSettings/GameCoreStudio.Project.json`, avoiding credential settings.
No per-installation HMAC key is read by the client. Stage verification checks candidate/project/source/
catalog/package/proposal/input bindings, retaining confinement/coldCache/verdictRef unchanged for admission.
The authenticated companion remains the only signature verifier; mandatory steps remain in VerdictCheck.

## Requests to other packets

- **R2-G**, `Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs`, registered
  `GenerateVoice(DialogueGraphDefinition graph, int node, string voice = "")`: replace the
  return line with `return GenerateVoice(graph, node, voice, ResolveMediaGateway());` and add
  `private static IMediaGenerationGateway ResolveMediaGateway()` using the same TypeCache/public
  parameterless discovery as AudioTools.MediaGateways.Resolve. This avoids a gameplay → Studio reference.
  Audio's existing TypeCache discovery now finds `GameCore.Studio.Etos.EtosMediaGenerator`; dialogue still hardcodes the null gateway.
- **R2-G / contracts owner**, `Packages/com.gamecore.gameplay.contracts/Runtime/Narrative/NarrativeSeams.cs`
  and `audio/Editor/AudioTools.cs`: the current IMediaGenerationGateway has only RequestVoiceLine.
  `audio.generateSfx` must keep its honest NotConfigured result until a shared sound-effect request
  signature and companion provider op exist. It cannot be wired by editing this packet's files alone.
- **R2-F**, `studio/agent/src/stage.rs`, signed passing record in `run_lane`: add `stageInputs: string[]`
  from the trusted resolved candidate input list before signing. The current record has no input list.
  `CompanionStageService.VerifyVerdict` refuses nonempty Expected.StageInputs when it is absent;
  zero-input records remain compatible. No fabricated input authority is added by the client.
- **R2-C**, candidate coordinator / GatewayExtras: consume ICandidateStageGateway.StageCandidateAsync
  with `StageAdmission.BuildStageRequest(candidate, runtime.Paths.ProjectRoot)`; poll Client.GetStageAsync,
  then `StageAdmission.FetchVerdict(jobId, originalRequest)`. Do not call the removed packageRef overload
  or authenticate CAS bytes through RecordVerdict. Show host confinement warning and require creator Admit.
- **R2-A**, `Runtime/Model/ChangeSetValidator.cs` / `ChangeSetValidationOptions` and
  `Editor/Engine/ChangeSetEngine.cs`: expose trusted project-service capabilities (for example
  `IReadOnlyCollection<string> ProjectCapabilities`) and satisfy a Project prerequisite from that
  set as well as authored nodes. Supply `agent.media` when `runtime.Services.AgentGateway?.IsConfigured`
  is true. The existing validator searches only `_index.Nodes`, and no index node provides agent.media;
  the contributor seam cannot add nodes/capabilities. Therefore audio/dialogue generation through
  generic Engine.Apply still hits MissingPrerequisite even after audio's direct registered tool
  discovers this adapter. R2-D tests the actual AudioTools registered method and its imported result,
  not a claim that this external engine prerequisite defect is solved.
- **Integrator / package-lock owner**, `games/hollowmere/Packages/packages-lock.json`, entry
  `com.gamecore.studio.etos.dependencies`: add `"com.gamecore.gameplay.contracts": "1.0.0"` to match this
  packet's new direct interface reference. Lockfile is outside R2-D's exclusive paths.

## Verification

- Client tests: 63 passed, 0 failed, 6 live skipped (69 total); `/tmp/r2-d-tests/r2-d-client-owned-upgrade.trx`.
  The sixth skip is the new optional R2-H candidate-stage qualification test.
- C# checker passed on the final implementation sources (1,101 files).
- Isolated defect-restoration build `/tmp/r2-d-regression-before/Before.csproj`, filter
  `FullyQualifiedName~R2RegressionTests`: 4 failed / 0 passed, as expected. Only the old legacy POST,
  queue-without-await, missing unsuccessful socket disposal, and raw/discarded error fields were
  restored in copied sources. This is a targeted defect-restoration check, not a historical full-build
  result. `/tmp/r2-d-tests/r2-d-before.trx` records the four failures; the actual checkout remains green.
- Initial Hollowmere EditMode XML `.unity-logs/r2-d.xml`: 26 total, 21 passed, 1 failed,
  4 skipped, 0 inconclusive. The failed `R2_21_CancelDuringTicketedConnectDisposesConnection`
  exposed Mono ignoring cancellation during a stalled upgrade. The client now explicitly aborts/disposes
  the pending socket, unblocks the caller on cancellation and observes any late task failure.
  First attempt had timed out in package resolve; the wrapper retry compiled and ran tests.
  The first warm rerun `.unity-logs/r2-d-verified.xml` again had 21 passed / 1 failed / 4 skipped:
  cancellation now returned promptly, but Mono retained the pending TCP upgrade even after Abort/Dispose.
  The Mono transport now owns the TCP/TLS upgrade and passes the verified stream to framework WebSocket
  framing; .NET retains ClientWebSocket. `ConnectWebSocketAsync` returns Task<WebSocket>. Final full warm rerun `.unity-logs/r2-d-owned.xml`: **26 total, 22 passed, 0 failed,
  4 skipped, 0 inconclusive**, Unity exit 0, 71 seconds wall time, 5.969 seconds test time. All seven
  R2-D tests and all 15 existing offline P2.2 tests passed. The four credential-gated live tests remain
  skipped; wrapper exit 1 correctly reports PARTIAL/NotRun for skipped acceptance rows.
  It disabled the silence watchdog for observed disk I/O stalls, kept the normal 1,500-second
  per-attempt deadline, and held one Editor slot.
- Metadata checker passed (41 packages, 89 assemblies) against Unity's generated lock update during
  the test run. That external generated file was restored after Unity exit to honor exclusive paths.
  The final clean-scope checkout therefore reports exactly one metadata failure: the Hollowmere
  lockfile dependency request above. No metadata rule or dependency declaration was weakened.
- `bash -n studio/tools/live-etos-tests.sh` and `git diff --check`: pass.
- Live runner uses the shared host `run-redacted.py` writer with explicit child environment before
  any log write. Local `/tmp/r2-d-live-writer-smoke.sh` passed split-token and nested-JSON checks;
  it runs only a synthetic Python child, never the live tests.

Exact final Unity invocation (from this clone):

```bash
UNITY_SILENCE_TIMEOUT=0 bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r2-d-owned --results "$PWD/.unity-logs/r2-d-owned.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.Etos.*|Hollowmere\.P2_2.*|GameCore\.Studio\.Hollowmere\.P2_2.*'
```

Result hashes (SHA-256):

- final Unity XML: `b6ba2e5a9b5b90fd44ca909e129bdcb773e7e57442650e651415abecf45c6b04`
- initial failed XML: `6ef685be97fcd8f66f3a2c439de8c4ee2da79b304a67ef4302ed9d3152c38a9f`
- intermediate failed XML: `81870cbb7a22b662270afcce5be7e3afbe0bdbf32a4fd3bb49623e46e6359e16`
- final dotnet TRX: `39ec0fd3924335eb898315a89ed941c1d930237893e6831af38a23f9707a7dbc`
- isolated defect-restoration TRX: `e3301475ece2b6e1b280af840f79d3ebd9681b7be58759d7760dafe9286ab5b4`

## Left open

- The core engine has no project-service capability seam for the tools' agent.media prerequisite.
  Direct audio tool discovery/import is exercised; engine dispatch needs the R2-A request above.
- Dialogue's hardcoded gateway and SFX's absent contract/provider are outside this packet's paths;
  exact requests are above. No successful dialogue default-path or SFX generation claim is made.
- Nonempty stage inputs cannot authenticate until R2-F includes them in the signed record.
- The external lockfile must be synchronized before the integrated metadata gate can pass.
- Mono's owned upgrade requires a direct node connection (`UseSystemProxy=false`, the existing production
  default). Explicit system-proxy requests refuse NotConfigured. TLS keeps platform certificate/hostname
  validation; no permissive certificate callback or raw WebSocket frame implementation is introduced.
- R2-H owns live qualification. No live ETOS test, provider charge, real killed-Editor scenario,
  live Stage → Admit, or sandbox child is run here. D3's actual Docker Unity licensing result is
  recorded by R2-F in `studio/agent/evidence/r2-f-sandbox-probe.txt` (no valid Unity licence, exit 198).
  This client neither opts into host confinement nor falls back to it.
