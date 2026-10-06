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

- `POST /v1/stage`: current R2-F HTTP JSON is `{changeSetId,projectId,sourceRevision,catalogRevision}` (optional operator lane selectors are documented in R2-F). The shared local DTO additionally holds sourceProject, packageDigest, proposalDigest and stageInputs for admission verification. The transport must project the HTTP fields explicitly: host paths and those additional local binding fields are not accepted as request authority. The companion resolves its configured project mapping and owned candidate/proposal ledger. Return 202 with jobId.
- `GET /v1/stage/{jobId}/verdict`: R2-F returns the complete signed record with top-level jobId/signature and verdict fields. The adapter retains that exact record and exposes it through the local `SignedVerdict` wrapper. Only a completed passing job has an issuable record. Signature is the companion's opaque installation HMAC, never a digest supplied by a worker.
- `POST /v1/stage/{jobId}/verify`: send the exact fetched signed record. Current R2-F returns `{verified:bool}` after checking caller ownership, HMAC, and equality with its stored record. The adapter maps the requested jobId into local `StageVerification` only after this authenticated response and checks the `StageVerificationRequest.Expected` candidate/project/source/catalog/package/proposal/input bindings against the signed record. Never authenticate by checking only that a signature is nonempty. The C# interface is a local service contract, not a direct serialization of the HTTP envelope.
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
stage.projects configuration. Transport adapters must project only the R2-F POST fields listed above; never send a host path
as authority or serialize the local verification wrapper directly onto the HTTP route.
R2-D still owns the concrete authenticated transport binding.

History integration and shared redaction are now present. The R2_15 regression calls the real
HistoryService, checks its default redo selection, and retains exact package preimages.

## R2-B2 — asynchronous admission smoke (PACKET.md)

Branch `codex/r2-b2`, Linux build host `myubuntu`. This appendix is the packet report;
no root PACKET.md is created because the exclusive documentation path is this file.
Only `Editor/Stage/**`, Hollowmere `Tests/R2_B/**`, and this appendix are edited.

### R2 fixes

- R2-14 / R2-G request #5: `AdmissionOptions.PollSmokeTest` has type
  `Func<StageVerdict, AdmissionSmokeStatus>?`, with `Pending`, `Passed`, `Failed`.
  The existing `SmokeTest` bool callback remains an immediate assertion. When both
  are bound, that assertion must pass before asynchronous polling begins; poll-only
  adapters are supported. Trusted game bootstrap must rebind after domain reload.
- Smoke polling uses idle `EditorApplication.update` frames. The durable
  `Studio/Admission/pending-cs_*.json` record keeps phase `smoke-pending`, status,
  start time, consumed frames and the selected limits. Limits default to
  `SmokeTestFrameBudget = 120` and `SmokeTestTimeoutSeconds = 60`; whichever is
  exhausted first fails with `smoke_budget_exhausted` and starts verified rollback.
  Limits cannot be reset by changing options or recreating the admission service.
- R2-15: Undo during a pending lifecycle refuses with `admission_pending`, including
  forced Undo through AdmissionHistoryHandler. History diagnostics use registered
  `Refused` with structured `data.reason = "admission_pending"`; direct admission
  results use `Reason = "admission_pending"`. Core HistoryService currently
  refuses Interrupted entries earlier with its existing `Refused` code (request
  below). The journal stays Interrupted with the
  admission scenario Pending; refusal does not advance the poll or emit Finished.

Regression fixture: `Hollowmere.R2_B.Tests.AdmissionSmokeTests`.

| Finding | Regression |
|---|---|
| R2-14 | `R2_14_PendingSmokePassesAcrossEditorFrames` |
| R2-14 | `R2_14_PendingSmokeFailureRollsBack` |
| R2-14 | `R2_14_PendingSmokeFrameBudgetExhaustion` |
| R2-14 | `R2_14_PendingSmokeWallBudgetSurvivesReloadWithoutTrust` |
| R2-14 | `R2_14_ReloadDuringPendingSmokeResumesWithRetainedBudget` |
| R2-14 | `R2_14_ReloadWithoutSmokeAdapterFailsClosed` |
| R2-14 | `R2_14_PollOnlySmokeNeedsNoSynchronousAdapter` |
| R2-14 | `R2_14_PendingSmokeExceptionFailsClosed` |
| R2-14 compatibility | `R2_14_SynchronousSmokeFailureStillRollsBackImmediately` |
| R2-15 | `R2_15_UndoRefusesWhileSmokePendingWithoutAdvancing(false/true)` |

### Requests to other packets

- **R2-G / P3.1**, `Packages/com.gamecore.gameplay.world/Editor/StudioAdmissionServices.cs`
  and trusted Hollowmere Editor bootstrap: bind
  `StageAdmission.Of(runtime).Options.PollSmokeTest = verdict => ...` using the
  tri-state signature above. Return Pending until the restored world's real frame
  assertions finish, Passed only after all assertions, Failed on failure. Keep
  callbacks idempotent across crash/reload and rebind with the active session;
  do not drive the world in a synchronous loop. The existing bool binding stays
  valid for immediate assertions. A missing poll adapter after reload must never
  fall back to a passing bool assertion.
- **Metadata/integration owner**, `Packages/com.gamecore.gameplay.world/package.json`:
  declare the already referenced `com.gamecore.studio.core: 1.0.0` and
  `com.unity.nuget.newtonsoft-json: 3.2.1` dependencies.
- **Metadata/integration owner**, `games/cleanproof/Packages/packages-lock.json`
  and `games/hollowmere/Packages/packages-lock.json`: synchronize the
  `com.gamecore.studio.etos` dependency map with its manifest, retaining
  `com.gamecore.studio.core: 1.0.0` and adding
  `com.gamecore.gameplay.contracts: 1.0.0`. These paths are outside this packet.

- **R2-A**, `Packages/com.gamecore.studio.core/Editor/Journal/HistoryService.cs`,
  `public HistoryResult Undo(string? changeSetId = null, bool force = false)`:
  after reading a non-null entry, dispatch `HistoryAction.Undo` to its registered
  admission handler **before** the generic Applied-only guard. Currently the guard
  returns `Refused` with null result State for Interrupted admissions, so our handler
  cannot supply `data.reason = "admission_pending"` and `State = Interrupted` through that public
  route. Preserve the generic guard for unhandled edit entries. Handler/direct
  admission refusals, both force values, and the current safe generic refusal are
  tested; no temporary Applied state or alternate history path was introduced.

### R2-G broad-run failure attribution

Read-only inspection of `/home/worlesenric/wkspace/gc-studio/r2-g/.unity-logs/r2-g-broad.xml`
confirmed 142 total, 140 passed, 1 failed, 1 skipped. Its only failed EditMode test was
`Hollowmere.P1_7b.EditMode.Tests.ToolJournalTests.Catalog_ExportsEveryNewTool_AndTheMediaToolsAreComposeToolsThatRequireAgentMedia`.
The assertion named `dialogue.generateVoice` and expected a prerequisite matching
its lambda. This is the obsolete authored `agent.media` expectation documented by
R2-G request #6. It is **not an R2-B/admission test**, and that sibling clone was
not modified.

### Left open

- Core HistoryService Undo rejects Interrupted before typed dispatch. It safely
  refuses without advancing the admission, but returning the admission-specific
  reason witness and Interrupted result State through that route needs the R2-A change above.
- The metadata checker has four inherited errors in the dependency files listed
  above. Exclusive path ownership prevents this packet from repairing them.
- Real Hollowmere restored-world smoke dispatch, real domain reload/Editor process
  kill, and the 90-second end-to-end admission budget require the game bootstrap
  and stage integration outside these paths. The regression fixtures run actual
  Editor update callbacks with deterministic compiler/catalog/capture doubles and
  rebuild StageAdmission from its durable record; they do not claim those external
  acceptance results.

### Verification (R2-B2)

The before run retained the new enum/options and tests but the original admission
behavior. XML `.unity-logs/r2-b2-before.xml`: **11 total, 1 passed, 10 failed,
0 skipped/inconclusive**. All asynchronous/undo regressions failed; the synchronous
bool failure compatibility case already passed. No baseline failure is counted
as acceptance.

Intermediate full selections are retained: `.unity-logs/r2-b2-admission.xml`
**143 passed / 3 failed** (Editor-update timing assumption and the two early
HistoryService-dispatch refusals), and `.unity-logs/r2-b2-final.xml`
**144 passed / 2 failed** (unregistered diagnostic codes were normalized to
ValidationFailed). Tests now wait for an observed Editor update; Stage emits the
registered Refused diagnostic with a structured admission_pending reason. All
135 pre-existing selected cases passed in both intermediate runs.

- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj`:
  **104 passed, 0 failed/skipped** on myubuntu (.NET 8.0.425).
- `python3 tools/check_game_core_csharp.py`: **pass, 1,130 C# files**.
- `git diff --check`: **pass**.
- Rust sources were not changed; no Rust, ETOS paid operation, credential-file read,
  service restart, or sibling-clone mutation was performed.

Unity commands use `bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere"
--log-dir "$PWD/.unity-logs" --label <label> --results <absolute XML> -- -runTests
-testPlatform EditMode -testFilter 'Hollowmere\.R2_B.*|Hollowmere\.P2_4.*|GameCore\.Studio\.Core.*'`.
The wrapper owns `-testResults` through `--results` and rejects a duplicate explicit
argument. This matches unity-compile.sh's test-run arguments and the host-wide
allocation protocol; this packet held at most one Editor. Counts come from XML.
The before selection used only `Hollowmere\.R2_B\.Tests\.AdmissionSmokeTests.*`.

XML SHA-256:

- before: `6b0b75a44b5abcaea9f0ea30c9f48f7e9e9d52ac4dab096eff5d9d58833dcdde`
- first intermediate: `53a202f9a561e423fffbdcc14521ef8d8eec874e09f7ad0c8aae6784b6b44ef1`
- R2-G broad XML (read-only attribution): `27aa52d464551717f19da33d83162be9fdaf5575981ef0aaa03a0a15c7da78d3`

Final full selection on implementation commit `57e11c49` (same source bytes as the
verified run): `.unity-logs/r2-b2-verified.xml`, **146/146 passed, 0 failed,
0 skipped/inconclusive**; R2-B **71/71** (60 existing + 11 new), P2.4 **8/8**, core
**67/67**. XML test duration 22.742 s; wrapper 65 s, one attempt. SHA-256:
`c88f8c6e6fdcb18a41dae73ec1f5c0478374e73a7c8ccdf1ef54dab16c31546d`.
Second intermediate XML SHA-256:
`a244ba21041b6ff6d0e82cfb1101f0b3a6b9848401d371d8a37473be89c56429`.
The implementation checkpoint was committed and pushed before this report commit.

Final metadata validation after restoring Unity-generated lockfile changes:
**failed, exactly the same four inherited errors** listed in Requests/Left open
(41 packages, 89 assemblies). No dependency manifests/locks were committed by this
packet. The existing untracked `.codex/` launcher directory was left untouched.

## R5 — R5-A Play-safe catalog capture

P4.2c request #3: `AdmissionLifecycle.Admit` no longer invokes the authored-scene
catalog rebake while Play is running. It durably captures and stops Play first. In
Edit mode, it retains the real catalog baseline in `pending` before package installation.
Signed-verdict refresh, context checks, compile/reload verification and rollback remain
mandatory. A cancellation or failed capture before any catalog baseline terminates without
installing/removing packages; the creator's capture is retained.

`Hollowmere.R5_A.P31AdmissionInPlayMode.R5_03_InstalledVerdict_CapturesInPlay_VerifiesCatalogBeforeInstall`
uses the actual installed P4.2c job, authenticated fetch/verify, real Hollowmere SaveService
and real `ReflectionAdmissionCatalog`. It replays that job's original signed context and
stops at the durable pre-install checkpoint. It does not claim a stage verdict for this
branch or use a catalog/service double. The original P3.1 fixture remains a separate
suite regression, with its documented test service/compiler/catalog doubles.

The retained passing service record has `catalogDelta.mechanisms` but no `world` or
`predicted`. Full admission will still refuse that incomplete catalog delta at the existing
verification check. Completing the current branch's full installed stage/admit requires a
new correctly bound stage job and the stage-owner fix; no authority check was weakened.
See [R5-A packet](../../../games/hollowmere/Assets/Hollowmere/Tests/R5_A/PACKET.md) for exact
requests, evidence and final counts.

R5-A final host verification on code `6638db2c`: **203/203 EditMode**, **8/8 P3_1 PlayMode**,
**1/1 separate-Editor reopen repeat**, **113/113 .NET Model**, and **2/2 adapter-boundary tests**.
All final XMLs have zero failures/skips/inconclusives; metadata, C# policy and whitespace checks pass.
The packet retains baseline failures, the Play startup timeout/retry, exact commands and hashes.
Full current-branch installed admission remains the explicit cross-packet follow-up above.
