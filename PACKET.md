# R2-D — Studio ETOS client and media integration

Branch `codex/r2-d`, Linux build host `myubuntu`. `git fetch origin && git merge origin/main`
reported already up to date at `de2d9593`. R2-A's former root note is now
`docs/studio/packets/R2-A-core-edit-recovery.md`; its published seams are consumed here.
No installed service, sibling clone, credential file, paid operation or live-node test is touched.

## R2 fixes

| Finding | Fix | Regression |
|---|---|---|
| R2-13 client / D5 | Four-field stage POST; project header on HTTP, artifact, ticket and WebSocket calls; exact signed record fetch/verify; IStageService and ICandidateStageGateway; persisted productGUID/path identity; local source/catalog callbacks rebound on session startup | `R2_13_StageUsesExactOwnedContractAndVerifiesUnmodifiedRecord`, `R2_13_ProjectIdentityPersistsAndTrustedContextRebinds` |
| R2-20 | Await main-thread handling before cursor save; stop cancels queued work; handler/save failure reconnects from last acknowledged cursor with `event_handler_failed` | `R2_20_UnhandledEventIsReplayedAfterStopAndThrowDoesNotAcknowledge`, `R2_20_MainThreadQueueMustHandleBeforeCursorSaveAndReloadReplays` |
| R2-21 | Socket ownership transfers only on successful connect; finally disposes every unsuccessful connection, including caller cancellation; stream shutdown aborts/disposes connected sockets | `R2_21_CancelTicketedConnectAndRepeatedReloadLeaveNoConnections`, `R2_21_CancelDuringTicketedConnectDisposesConnection` |
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
  `new NotConfiguredMediaGateway()` argument with `MediaGateways.Resolve()` (with its audio namespace
  qualified, or an equivalent contracts-owned discovery seam). Audio's existing TypeCache discovery
  now finds `GameCore.Studio.Etos.EtosMediaGenerator`; dialogue still hardcodes the null gateway.
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

- Client tests: 62 passed, 0 failed, 6 live skipped (68 total); `/tmp/r2-d-tests/r2-d-client-final.trx`.
  The sixth skip is the new optional R2-H candidate-stage qualification test.
- C# checker passed at the implementation checkpoint (1,099 files).
- Unity EditMode is pending a host-wide slot; exact final XML/counts will be recorded below.
- Metadata checker currently reports only the external Hollowmere lockfile dependency request above.
- `bash -n studio/tools/live-etos-tests.sh` and `git diff --check`: pass.

## Left open

- The core engine has no project-service capability seam for the tools' agent.media prerequisite.
  Direct audio tool discovery/import is exercised; engine dispatch needs the R2-A request above.
- Dialogue's hardcoded gateway and SFX's absent contract/provider are outside this packet's paths;
  exact requests are above. No successful dialogue default-path or SFX generation claim is made.
- Nonempty stage inputs cannot authenticate until R2-F includes them in the signed record.
- The external lockfile must be synchronized before the integrated metadata gate can pass.
- R2-H owns live qualification. No live ETOS test, provider charge, real killed-Editor scenario,
  live Stage → Admit, or sandbox child is run here. D3's actual Docker Unity licensing result is
  recorded by R2-F in `studio/agent/evidence/r2-f-sandbox-probe.txt` (no valid Unity licence, exit 198).
  This client neither opts into host confinement nor falls back to it.
