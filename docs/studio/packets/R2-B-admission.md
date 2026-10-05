# R2-B — admission and verdict trust

Branch: `codex/r2-b`. Exclusive implementation paths: core `Editor/Stage/**`, Hollowmere `Tests/R2_B/**`, and P2_4 admission tests. Shared packet notes only receive the R2-B section. No sibling clone or installed service is modified.

## Contracts published to other packets

`StageCandidateRequest` is shared in `GameCore.Studio.Authoring.Agent`; the other types below are in `GameCore.Studio.Edit`, `GameCore.Studio.Core.Editor`.

### R2-D / R2-F: authenticated stage service

`IStageService`:

```csharp
Task<string> RequestStage(StageCandidateRequest request);
Task<SignedVerdict> GetVerdict(string jobId);
Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request);
```

Use the paired companion transport with its app authentication and `X-GameCore-Project: <stable project id>` on **every** call. Owners are `(authenticated app, project header)`; another owner's job or candidate is 404, including verify. Never accept a worker-supplied service, project mapping, signature key, or verify response.

- `POST /v1/stage`: JSON `{changeSetId,projectId,sourceProject,sourceRevision,catalogRevision,packageDigest,proposalDigest,stageInputs:[...]}`. Digests are lowercase SHA-256 hex without `sha256:`. No `packageRef`, arbitrary `steps`, unsafe exemption, or host-confinement switch. `sourceProject` is the trusted client's project mapping. The companion loads the candidate/proposal from its owned ledger and checks the request digests and declared data inputs against it. Return 202 `{jobId}`; this invokes the seven-step change-set lane.
- `GET /v1/stage/{jobId}/verdict`: return 200 `{jobId,signature,verdict:{...}}` only for the completed companion-issued record. Pending is 409 `stage_pending`; failed/unavailable sandbox produces `stage_failed` without an issuable verdict. `signature` is the companion's opaque HMAC, never a digest supplied by a worker.
- `POST /v1/stage/{jobId}/verify`: body `{signedVerdict:{jobId,signature,verdict},expected:<the original StageCandidateRequest>}`. Verify the stored job and caller ownership, HMAC, and **every signed field** against the stored record. Match expected job/project/change-set/source/catalog/package/proposal/input bindings. Return 200 `{verified:true,jobId}` only for an authentic completed record; invalid records return `{verified:false,jobId}` or an HTTP refusal. Do not simply echo client fields or check that a signature is nonempty. A persisted job cannot be replaced with another revision's verdict.
- The verdict uses `gamecore.studio.stage-verdict/1`, additionally requiring `jobId`, `projectId`, `sourceRevision`, `catalogRevision`, `confinement:"docker"|"host"`, `coldCache:boolean`. Existing `artifacts` package/proposal digests and `files` must describe exact bytes. Require `pass:true`, no partial/failure, explicit empty `forbiddenHits`, and exactly one passing result for each `scan`, `checkers`, `dotnet`, `unity-editmode`, `playmode-smoke`, `determinism`, `budget`. `budgetMs=360000`; warm jobs cannot exceed it. Cold-cache jobs may exceed once as enforced by the runner. HMAC covers all fields, including confinement/coldCache, step results, file hashes and catalog delta. The companion owns cache versioning by Unity version and kernel/gameplay version hash.

`StageAdmission.BuildStageRequest(candidate, trustedSourceProject)` validates paths and builds the DTO. Configure `Options.StageService`, `ProjectId`, `SourceRevision`, `CatalogRevision` from trusted project context. `await admission.FetchVerdict(jobId, originalRequest)` fetches, verifies and retains an authorized in-memory verdict. `RecordVerdict(byte[])` is compatibility **evidence only**; `Admit(candidate, verdictBytes)` refuses. No local HMAC key is handed to Unity. After a reload, `RefreshPendingVerdicts()` authenticates pending jobs again; persisted bytes alone never regain trust. Configure `AllowHostConfinement` only from an explicit operator choice; default false.

### R2-A / R2-C / R2-E: generic history dispatch

Core owns `IHistoryEntryHandler` in `Editor/Journal/`:

```csharp
HistoryEntryKind Kind { get; } // Admission
HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force);
```

`StageAdmission` registers `AdmissionHistoryHandler` through `runtime.History.RegisterHandler(...)`
on every runtime creation/rebuild. Undo/Redo/Resume/Rollback all route through `runtime.History`.
The durable admission lifecycle retains Interrupted while Pending; its Finished event updates the
redo stack only after verified Admitted/Undone. Force never bypasses provenance or ownership.
Internal package installation/removal is performed by the admission state machine after its durable
checkpoint (there are no discoverable mutation tools or generic engine replay).

### R2-G: services after game/domain reload

Game bootstrap must rebind the current project's `StageAdmission.Of(StudioServices.Runtime).Options` on every Editor/domain and game session startup: `Capture = new SaveServiceAdmissionCapture(() => activeSaveService)`, `SessionReady = () => active restored-capable world is ready`, and `SmokeTest = verdict => run this verified proposal's live smoke assertions`. `StartPlayMode`/`StopPlayMode` default to Unity's play flag. Only a ready session can restore; missing services keep the durable entry Pending with an explicit diagnostic. Smoke adapters must be idempotent across crash recovery. Do not install callback types from candidate JSON. The compiler/catalog/checker defaults are rebuilt after reload; trusted client/game services must re-register.

## Requests to other packets

- **R2-D**, `Packages/com.gamecore.studio.etos/Client/CompanionClient.cs` and Editor adapter: implement the exact async `IStageService` HTTP contract above; bind trusted project/source/catalog context after every reload. Replace artifact `RecordVerdict` calls with `FetchVerdict`. Refresh a retained job before redo after a fresh domain.
- **R2-F**, `studio/agent/src/stage.rs` and stage verdict/router modules: implement the signed record and verify route above, scoped ownership, complete-step enforcement, Docker sandbox, versioned warm cache, and real Docker Unity licensing evidence. Never issue an unsigned or partial record.
- **R2-A**, `Packages/com.gamecore.studio.core/Editor/Journal/HistoryService.cs`: dispatch matching `IHistoryEntryHandler` before generic undo/redo/recovery; register `new AdmissionHistoryHandler(StageAdmission.Of(runtime))`. Preserve redo bookkeeping until asynchronous `StageAdmission.Finished` reports final Undone/Admitted. Reconcile the provisional interface into the agreed core location without duplicate declarations. Generic history must refuse an admission if no handler is registered; it must not mark it undone merely because no generic inverse is present.
- **R2-C**, candidate coordinator and panel: build and send `StageCandidateRequest`, store job/request, fetch verified verdict, then enable the explicit creator Admit action. Show signed confinement and coldCache; host mode requires explicit opt-in and a warning. Route all history actions through R2-A dispatch.
- **R2-G**, new `Packages/com.gamecore.gameplay.world/Editor/StudioAdmissionServices.cs`: expose `public static void BindAdmission(StudioRuntime runtime, Func<SaveService?> activeSaveService, Func<bool> sessionReady, Func<StageVerdict,bool> smokeTest)` and have the trusted game bootstrap call it after every domain/game-session startup. Bind the capture/session/smoke options above to the active restored root. A lambda captured only before compile does not survive reload. Smoke receives the verified verdict and may resolve its retained proposal digest through the runtime artifact store; never invoke an unverified proposal.
- **R2-A**, `Packages/com.gamecore.studio.core/Runtime/Authoring/StudioLog.cs`: extend the existing `SecretRedactor.Redact(string)` to D9's full prefix/JSON-key coverage. Stage verdict summaries, admission/compile result details, journal scenarios and checker diagnostics now call that shared implementation; no Stage-specific redactor is introduced. R2-F must redact child output before persistence/signing.

## Fixes and tests

- R2-09: authenticated fetch/verify only, job/context/digest binding, mandatory steps, partial/forbidden/budget and confinement checks. `R2_09_*` tests cover raw self-authored bytes, unsigned/mismatched jobs, companion rejection, incomplete verdicts and revision changes. Existing P2_4 artifact-mismatch tests migrate to a fake authenticated service.
- R2-10: no public mutation catalog attributes; reserved packages and project admission ownership checked. `R2_10_MutationToolsAbsentFromCatalogAndDirectApplyRefuses`.
- R2-08: candidate file basenames, ancestor symlink checks and containment before reads. `R2_08_ArtifactNameCannotReadOutsideCandidate`, `R2_08_SymlinkedCandidateArtifactRefusesBeforeRead`.
- R2-12 C#: data-only stage input extensions; no Editor/plugins/traversal/directories; contained proposal rules paths. `R2_12_StageInputDataOnly`, `R2_12_ProposalPathsContained`.
- R2-13: one typed candidate stage request. `R2_13_TypedRequestPreservesCandidateAndBinding`.
- R2-14: durable pre-effect records under `Studio/Admission`; fresh trust after reload; compile/reload/rebake/verify/apply and undo recovery. `R2_14_CrashAtEachTransitionResumes`, `R2_14_CrashDuringUndoRetainsRecovery`, `R2_14_CaptureStopAndRestoreResumeAfterCrash`, `R2_14_AsyncStopAutomaticallyResumesFullCandidate`, `R2_14_FailedUndoKeepsInterruptedRecordAndPreimages`, `R2_14_LegacyAfterPlayRecordIsDetectedAndRefused`, migrated P2_4 capture/rollback tests.
- R2-15: `AdmissionHistoryHandler` handles undo/redo/resume/rollback, retains exact package preimages and verifies compile/catalog. `R2_15_HistoryHandlerDispatchesUndoAndRedoWithExactPreimages`. This tests the generic **interface dispatch**, not the unmodified external HistoryService integration.

## Verification

Host: myubuntu, Unity 6000.0.75f1, .NET SDK 8.0.425. No paid ETOS operations, credential reads, installed-service changes, or sibling-clone changes.

| Run | Result |
|---|---|
| Initial broad EditMode run | XML: 177 total, 173 passed, 0 failed, 4 skipped, 0 inconclusive; cold Editor 746 s |
| Broad rerun at implementation checkpoint `0f99cc59` | XML: 178 total, 174 passed, 0 failed, 4 skipped, 0 inconclusive; Editor 210 s. R2-B 57/57, P2_4 8/8, existing Studio 109 passed / 4 skipped |
| Capture-idempotence follow-up | XML: 66/66 passed, 0 failed/skipped; Editor 40 s. Includes a crash after capture and before the stop-play checkpoint |
| Final focused admission run at `5b1e37fc` | XML: 67/67 passed (R2-B 59/59 + P2_4 8/8), 0 failed/skipped/inconclusive; Editor 55 s. Includes verdict/admission/journal shared-redactor regression |
| `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj` | 101 passed, 0 failed/skipped. Initial `--no-restore` attempt had no assets file; rerun with restore passed |
| `python3 tools/check_package_metadata.py` | Pass: 41 packages, 88 assemblies, exact dependency sets |
| `python3 tools/check_game_core_csharp.py` | Pass: 1,081 files |
| `git diff --check` | Pass |
| Docker Unity probe, no network/live-project mount | `sandbox_unavailable`, exit 127 in 5 s; `libgtk-3.so.0` and `libgdk-3.so.0` missing. Licensing not reached, no verdict or fallback |

Unity ran through `bash studio/tools/unity-batch.sh` under its host-wide lock, holding at most one Editor slot. Broad arguments: `-runTests -testPlatform EditMode -testFilter '(Hollowmere\.(P2_4|R2_B)|GameCore\.Studio)\..*' -testResults <xml>`; the follow-up uses `Hollowmere\.(P2_4|R2_B)\..*`. Results are read from XML, not the wrapper's summary. Evidence files are `.unity-logs/r2-b-initial-results.xml`, `r2-b-final-results.xml`, `r2-b-capture-results.xml`, and `r2-b-admission-results.xml`. The four skipped tests are the existing credential-gated live ETOS NPC request, media, spoken-WAV voice and microphone tests; no skip is counted as a pass. The first direct wrapper invocation lacked executable permission; invoking via bash resolved it without editing the shared script.

Result XML SHA-256 (unmodified host evidence):

- `r2-b-final-results.xml`: `495a303cf964d4b310e4c80b8ff5950faf0748759eb6b40995dde256fc3c8065`
- `r2-b-capture-results.xml`: `448dca36add876d5f185486450b1f73330f8c0b1519ef46dda97462337e3a6ab`
- `r2-b-admission-results.xml`: `ef7c8f68e76706ad425fea1b9d1fb69c8c0e03504f1b8c9c09d8089ff0002666`

Capture retry is explicitly idempotent: `SaveServiceAdmissionCapture` validates/reuses the original saved checkpoint, refuses an incomplete existing checkpoint, and never overwrites it after a crash. Custom capture adapters must implement the same contract.

## Left open

- R2-A generic HistoryService dispatch, R2-D authenticated transport and R2-F signed routes are outside this packet's exclusive paths. The implementation fails closed until these adapters are integrated. Their production integration cannot be claimed from test doubles.
- R2-G's production capture/session/smoke registration is outside the exclusive paths; real Hollowmere resumed-play continuity and the 90-second budget require that integration. Missing adapters retain Pending instead of reporting success.
- Legacy `admit-after-play-*` records from the old code omit candidate/job provenance. They are detected and refused with `legacy_admission_incomplete`; automatically admitting them would recreate R2-09. Restage and explicitly Admit. Both the old Library location and the new Studio location are scanned. New capture requests persist the full pending record before capture/stop.
- D9: the baseline shared core redactor lacks the full new token-prefix and JSON key-name rules. R2-A owns that expansion; Stage now uses the existing shared implementation for its display and diagnostic text.
- D3: Docker 29.6.2 ran an offline, read-only-root probe using `localhost/gc-mechanic:current`, a read-only Unity Editor mount, slot-local HOME/write mount, no live project, and a read-only licence mount when present. Under the host-wide Unity lock it exited 127 in 5 s: `sandbox_unavailable: loader dependency missing (`libgtk-3.so.0`, `libgdk-3.so.0`); Unity licensing not reached`. The evidence writer discarded raw child output and recorded only fixed classification strings in `.unity-logs/docker-probe/r2-b-docker-license-20261005T143407-a1.log`. No verdict was issued and no host fallback was attempted for the probe. R2-F must supply the sandbox image dependencies and rerun licensing; host execution of trusted tests is not evidence of candidate confinement. Default admission refuses host verdicts.

## R2-int1 contract reconciliation

The provisional Stage interface is removed. `StageCandidateRequest` retains R2-A's local
ProjectPath/RepoRoot context and R2-B's sourceProject, artifact digests and validated stageInputs.
The six-argument local-context constructor has no artifact bindings; use BuildStageRequest before
requesting admission. The full constructor is used for persisted admission verification records.

R2-F's current HTTP boundary rejects sourceProject paths and resolves projectId from operator
stage.projects configuration. Transport adapters must omit sourceProject (as well as local-only
ProjectPath/RepoRoot) from POST /v1/stage; never send a host path as authority. The earlier POST
shape above describes the local admission DTO, not an authorization to override that mapping.
R2-D still owns the concrete authenticated transport binding.

History integration and shared redaction are now present. The R2_15 regression calls the real
HistoryService, checks its default redo selection, and retains exact package preimages.
