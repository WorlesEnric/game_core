# R2-B — admission and verdict trust

Branch: `codex/r2-b`. Exclusive implementation paths: core `Editor/Stage/**`, Hollowmere `Tests/R2_B/**`, and P2_4 admission tests. Shared packet notes only receive the R2-B section. No sibling clone or installed service is modified.

## Contracts published to other packets

All types below are in `GameCore.Studio.Edit`, `GameCore.Studio.Core.Editor`.

### R2-D / R2-F: authenticated stage service

`IStageService`:

```csharp
Task<string> RequestStage(StageRequest request);
Task<SignedVerdict> GetVerdict(string jobId);
Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request);
```

Use the paired companion transport with its app authentication and `X-GameCore-Project: <stable project id>` on **every** call. Owners are `(authenticated app, project header)`; another owner's job or candidate is 404, including verify. Never accept a worker-supplied service, project mapping, signature key, or verify response.

- `POST /v1/stage`: JSON `{changeSetId,projectId,sourceProject,sourceRevision,catalogRevision,packageDigest,proposalDigest,stageInputs:[...]}`. Digests are lowercase SHA-256 hex without `sha256:`. No `packageRef`, arbitrary `steps`, unsafe exemption, or host-confinement switch. `sourceProject` is the trusted client's project mapping. The companion loads the candidate/proposal from its owned ledger and checks the request digests and declared data inputs against it. Return 202 `{jobId}`; this invokes the seven-step change-set lane.
- `GET /v1/stage/{jobId}/verdict`: return 200 `{jobId,signature,verdict:{...}}` only for the completed companion-issued record. Pending is 409 `stage_pending`; failed/unavailable sandbox produces `stage_failed` without an issuable verdict. `signature` is the companion's opaque HMAC, never a digest supplied by a worker.
- `POST /v1/stage/{jobId}/verify`: body `{signedVerdict:{jobId,signature,verdict},expected:<the original StageRequest>}`. Verify the stored job and caller ownership, HMAC, and **every signed field** against the stored record. Match expected job/project/change-set/source/catalog/package/proposal/input bindings. Return 200 `{verified:true,jobId}` only for an authentic completed record; invalid records return `{verified:false,jobId}` or an HTTP refusal. Do not simply echo client fields or check that a signature is nonempty. A persisted job cannot be replaced with another revision's verdict.
- The verdict uses `gamecore.studio.stage-verdict/1`, additionally requiring `jobId`, `projectId`, `sourceRevision`, `catalogRevision`, `confinement:"docker"|"host"`, `coldCache:boolean`. Existing `artifacts` package/proposal digests and `files` must describe exact bytes. Require `pass:true`, no partial/failure, explicit empty `forbiddenHits`, and exactly one passing result for each `scan`, `checkers`, `dotnet`, `unity-editmode`, `playmode-smoke`, `determinism`, `budget`. `budgetMs=360000`; warm jobs cannot exceed it. Cold-cache jobs may exceed once as enforced by the runner. HMAC covers all fields, including confinement/coldCache, step results, file hashes and catalog delta. The companion owns cache versioning by Unity version and kernel/gameplay version hash.

`StageAdmission.BuildStageRequest(candidate, trustedSourceProject)` validates paths and builds the DTO. Configure `Options.StageService`, `ProjectId`, `SourceRevision`, `CatalogRevision` from trusted project context. `await admission.FetchVerdict(jobId, originalRequest)` fetches, verifies and retains an authorized in-memory verdict. `RecordVerdict(byte[])` is compatibility **evidence only**; `Admit(candidate, verdictBytes)` refuses. No local HMAC key is handed to Unity. After a reload, `RefreshPendingVerdicts()` authenticates pending jobs again; persisted bytes alone never regain trust. Configure `AllowHostConfinement` only from an explicit operator choice; default false.

### R2-A / R2-C / R2-E: generic history dispatch

R2-B provisionally declares `IHistoryEntryHandler` in `Editor/Stage/AdmissionHistoryHandler.cs`:

```csharp
bool CanHandle(ChangeSet entry);
HistoryResult Undo(ChangeSet entry, bool force);
HistoryResult Redo(ChangeSet entry);
HistoryResult Resume(ChangeSet entry);
HistoryResult Rollback(ChangeSet entry);
```

`AdmissionHistoryHandler(StageAdmission)` implements all methods. It recognizes admit/remove journal entries. Mutation tools have no `AuthorOperation` attributes and are absent from the registry; do not re-add them to make generic replay work. `StageAdmission` performs the internal journaled file operation directly. Admission `Pending` means the journal remains Interrupted, never Applied/Undone. Force cannot bypass ownership or provenance.

### R2-G: services after game/domain reload

Game bootstrap must rebind the current project's `StageAdmission.Of(StudioServices.Runtime).Options` on every Editor/domain and game session startup: `Capture = new SaveServiceAdmissionCapture(() => activeSaveService)`, `SessionReady = () => active restored-capable world is ready`, and `SmokeTest = verdict => run this verified proposal's live smoke assertions`. `StartPlayMode`/`StopPlayMode` default to Unity's play flag. Only a ready session can restore; missing services keep the durable entry Pending with an explicit diagnostic. Smoke adapters must be idempotent across crash recovery. Do not install callback types from candidate JSON. The compiler/catalog/checker defaults are rebuilt after reload; trusted client/game services must re-register.

## Requests to other packets

- **R2-D**, `Packages/com.gamecore.studio.etos/Client/CompanionClient.cs` and Editor adapter: implement the exact async `IStageService` HTTP contract above; bind trusted project/source/catalog context after every reload. Replace artifact `RecordVerdict` calls with `FetchVerdict`. Refresh a retained job before redo after a fresh domain.
- **R2-F**, `studio/agent/src/stage.rs` and stage verdict/router modules: implement the signed record and verify route above, scoped ownership, complete-step enforcement, Docker sandbox, versioned warm cache, and real Docker Unity licensing evidence. Never issue an unsigned or partial record.
- **R2-A**, `Packages/com.gamecore.studio.core/Editor/Journal/HistoryService.cs`: dispatch matching `IHistoryEntryHandler` before generic undo/redo/recovery; register `new AdmissionHistoryHandler(StageAdmission.Of(runtime))`. Preserve redo bookkeeping until asynchronous `StageAdmission.Finished` reports final Undone/Admitted. Reconcile the provisional interface into the agreed core location without duplicate declarations. Generic history must refuse an admission if no handler is registered; it must not mark it undone merely because no generic inverse is present.
- **R2-C**, candidate coordinator and panel: build and send `StageRequest`, store job/request, fetch verified verdict, then enable the explicit creator Admit action. Show signed confinement and coldCache; host mode requires explicit opt-in and a warning. Route all history actions through R2-A dispatch.
- **R2-G**, gameplay world Editor bootstrap: register the capture/session/smoke adapters above after domain reload. A lambda captured only before compile does not survive reload.
- **R2-A**, shared core redactor seam: provide its exact callable signature for Stage's existing checker/compile diagnostics and evidence writers; R2-B avoids including exception details from untrusted input in new diagnostics.

## Fixes and tests

- R2-09: authenticated fetch/verify only, job/context/digest binding, mandatory steps, partial/forbidden/budget and confinement checks. `R2_09_*` tests cover raw self-authored bytes, unsigned/mismatched jobs, companion rejection, incomplete verdicts and revision changes. Existing P2_4 artifact-mismatch tests migrate to a fake authenticated service.
- R2-10: no public mutation catalog attributes; reserved packages and project admission ownership checked. `R2_10_MutationToolsAbsentFromCatalogAndDirectApplyRefuses`.
- R2-08: candidate file basenames, ancestor symlink checks and containment before reads. `R2_08_ArtifactNameCannotReadOutsideCandidate`, `R2_08_SymlinkedCandidateArtifactRefusesBeforeRead`.
- R2-12 C#: data-only stage input extensions; no Editor/plugins/traversal/directories; contained proposal rules paths. `R2_12_StageInputDataOnly`, `R2_12_ProposalPathsContained`.
- R2-13: one typed candidate stage request. `R2_13_TypedRequestPreservesCandidateAndBinding`.
- R2-14: durable pre-effect records under `Studio/Admission`; fresh trust after reload; compile/reload/rebake/verify/apply and undo recovery. `R2_14_CrashAtEachTransitionResumes`, `R2_14_CrashDuringUndoRetainsRecovery`, migrated P2_4 capture/rollback tests.
- R2-15: `AdmissionHistoryHandler` handles undo/redo/resume/rollback, retains exact package preimages and verifies compile/catalog. `R2_15_HistoryHandlerDispatchesUndoAndRedoWithExactPreimages`. This tests the generic **interface dispatch**, not the unmodified external HistoryService integration.

## Verification

In progress. Unity runs through `bash studio/tools/unity-batch.sh` and its host-wide slot lock, using `-runTests -testPlatform EditMode -testFilter '(Hollowmere\.(P2_4|R2_B)|GameCore\.Studio)\..*' -testResults <xml>`. Pass/fail is read from the XML. Initial direct script execution lacked executable permission; invoking via bash resolves that without changing the shared script.

## Left open

- R2-A generic HistoryService dispatch, R2-D authenticated transport and R2-F signed routes are outside this packet's exclusive paths. The implementation fails closed until these adapters are integrated. Their production integration cannot be claimed from test doubles.
- R2-G's production capture/session/smoke registration is outside the exclusive paths; real Hollowmere resumed-play continuity and the 90-second budget require that integration. Missing adapters retain Pending instead of reporting success.
- Legacy `admit-after-play-*` records from the old code omit candidate/job provenance. They are detected and refused with `legacy_admission_incomplete`; automatically admitting them would recreate R2-09. Restage and explicitly Admit. New capture requests persist the full pending record before capture/stop.
- D3: Docker daemon is available (29.6.2); R2-B does not own the sandbox runner. A Docker Unity license outcome is not yet established here. Host execution of trusted tests is not evidence of candidate sandbox confinement; default admission refuses host verdicts.
