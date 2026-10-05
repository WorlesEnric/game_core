# R2-A — core edit engine, recovery and artifact authority

Branch: `codex/r2-a`. Build host: myubuntu. Scope: studio.core excluding Editor/Stage; core package tests. No sibling clone, companion service, ETOS node operation or credential file was accessed.

## R2 fixes

| Finding | Implementation | Regression test in `R2CoreRegressionTests` |
|---|---|---|
| R2-01 | Hidden unsaved preview transform; begin-drag stamp retained; only engine commit changes target | `R2_01_SaveAndReloadDuringDragPreserveRealTransform` |
| R2-02 | Shared importer-settings check before writes; retain bytes/meta before import; failed results retain inverse | `R2_02_InvalidImporterLeavesExistingBytesAndMetaUnchanged` |
| R2-03 | `EditContext.PrepareInverse(Operation[], bool)` durably checkpoints preimages before writes; file-write fault hook; history transition checkpoint before inverse work | `R2_03_CrashInsideFileWriteHasDurablePreimage`, `R2_03_UndoCrashCheckpointsBeforeInverseMutation`, `R2_03_RedoResumeRetainsOriginalCreationIdentity`, `R2_03_RecoveryKeepsTransitionUntilJournalCheckpoint` |
| R2-04 | Per-inverse recovery checkpoints; failures preserve Interrupted and inverse evidence; resume distinguishes successful dependencies; original AllOrNothing preimages rolled back on resume failure | `R2_04_FailedInverseRemainsInterruptedWithEvidence`, `R2_04_ResumeDoesNotDropFailedDependency`, `R2_04_ResumeFailureRollsBackEarlierAtomicSuccess` |
| R2-05 | Apply checks staged catalog revision and every baseVersions read | `R2_05_ReadDependencyChangedAfterStageRefusesApply`, `R2_05_CatalogChangedAfterStageRefusesApply` |
| R2-06 | Translated live actions do not invoke authored tool; non-undoable runtime journal entries; atomic runtime batches refused; RuntimeOnly catalog metadata validates mixed batches | `R2_06_RuntimeActionsDoNotWriteAuthoredDataAndCannotUndo`, `R2_06_AtomicRuntimeMixIsRefusedBeforeWorldSubmit`; existing `LiveEditTests` updated to D4 |
| R2-07 | Default-deny raw-byte media allowlist shared by import/bind; Editor/Plugins and linked destinations refused | `R2_07_ExecutableAndHookPathsRefusedBeforeWrite` (10 cases) |
| R2-08 core | Artifact digests only; source/sha256 host-file arguments removed from catalog and never read | `R2_08_SourcePathNeverReadAndAbsentFromCatalog` |
| R2-13 core | Typed `StageCandidateRequest` / `ICandidateStageGateway` seam | `R2_13_TypedStageRequestRetainsCandidateAndProjectContext`; consumer integration requested below |
| R2-15 core | HistoryService dispatches Undo/Redo/Resume/Rollback to per-runtime IHistoryEntryHandler keyed by HistoryEntryKind | `R2_15_AllHistoryActionsUseTypedAdmissionHandler` |
| R2-22 core | Shared SecretRedactor covers all ETOS prefixes, sk-, bearer, recursive JSON secret keys; RedactingTextWriter buffers split tokens before sink writes | `R2_22_SharedRedactorMasksNestedJsonAndSplitChildOutput` |
| R2-33 core | ReadOnly model/mirror metadata flows to ToolEntry and ReflectedTool | `R2_33_ReadOnlyMetadataFlowsThroughRegistry` |
| R2-36 core | Default NestedReferenceContributor installed in StudioRuntime; nested refs and field labels in node projection; canonical endpoint resolution | `R2_36_CoreReferencesInstalledBeforeAnyView` |
| R2-39 core | Production TypeCache discovery excludes test/fixture/NUnit assemblies; tests inject sources explicitly | `R2_39_ProductionDiscoveryExcludesFixtures` |

## Requests to other packets

- **R2-B**, `Editor/Stage/StageAdmission.cs`: implement `IHistoryEntryHandler` (`Kind => HistoryEntryKind.Admission`, `HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force)`) and register through `runtime.History.RegisterHandler(handler)` on every runtime rebuild. All admission transitions must retain their durable compile/reload state. Core refuses admission history when no handler is installed. Internal admit/remove tools must call `EditContext.PrepareInverse` before package side effects. C/E must call HistoryService, without special-casing admission.
- **R2-C/D/F**, UI CandidateCoordinator / ETOS gateway / companion stage route: consume `GameCore.Studio.Authoring.Agent.StageCandidateRequest(changeSetId, projectId, projectPath, repoRoot, sourceRevision, catalogRevision)` through `ICandidateStageGateway.StageCandidateAsync(request, cancellationToken)`. Project paths must come from operator configuration, never candidate JSON. `FetchTrustedVerdictAsync(jobId, cancellationToken)` must use authenticated `/v1/stage/{job}/verdict`; retain returned signed record and verdict reference. No core consumer authenticates arbitrary artifact bytes.
- **R2-G**, `com.gamecore.gameplay.world/Editor/WorldTools.cs`: register production `ILiveOpTranslator` through `runtime.Services.RegisterLiveTranslator(translator)` after runtime creation/reload (unregister supported); `CanTranslate(EditContext)` identifies runtime actions and `CompositionEditPayload? Translate(EditContext, out Diagnostic?)` returns the validated world edit. Mark action metadata `RuntimeApplicability = RuntimeApply.Live, RuntimeOnly = true` in shared/mirror attributes. Core records world operation IDs, never calls the authored tool for translated actions, and refuses multi-op AllOrNothing live batches without an atomic world gateway. Apply-to-authored must be a separate persistent change set.
- **R2-E**, Views `NestedReferenceContributor.cs` / `StudioViewContext.cs`: remove the Views contributor and registration/rebuild side effect; consume `runtime.References` (`GameCore.Studio.Edit.NestedReferenceContributor`) and node `Refs` field labels. Opening a view must not change core semantic coverage. Mark pure tools ReadOnly in shared/mirror metadata, and call `runtime.Registry.Invoke(toolId, target, args)`.
- **R2-D/F/G**, errors/log capture/evidence: use `SecretRedactor.Redact(string)`, `RedactJson(JToken)` and `RedactingTextWriter(TextWriter)` at sinks, without retaining raw inner transport exceptions. Flush keeps an incomplete line private until newline/disposal to prevent token fragments from leaking.
- **Integrator**, `dotnet/tests/GameCore.Studio.Model.Tests/JsonRoundTripTests.cs`, `DiagnosticCodeRegistryIsComplete`: extend the exact expected registry with `MediaTypeForbidden`, `MediaPathForbidden`, `MediaImporterInvalid`, `ArtifactSourceForbidden`. This test file is outside R2-A exclusive paths. Companion code-registry mirrors should accept the same four codes.
- **R2-C**, `Editor/Gizmo/ViewportMoveGizmo.cs`: while dragging, project `GizmoMoveController.PreviewTransform.position` for the handle origin instead of the real target transform; core Scene-view EditorTool already consumes this preview seam.
- **R2-H**, `docs/studio/schemas/tool-catalog.schema.json`: regenerate schemas from the updated core ToolEntry (optional boolean `readOnly` and `runtimeOnly`, default false). File is outside R2-A ownership. Corresponding mirror changes are required in `studio/agent/src/model.rs` (R2-F). The integrator must add `public bool ReadOnly { get; set; }` and `public bool RuntimeOnly { get; set; }` to `Packages/com.gamecore.gameplay.contracts/Runtime/AuthoringMetadata.cs` / `AuthorOperationAttribute` (outside every R2-A code path), so gameplay tools can declare these flags. Existing catalog entries serialize identically when both flags are false.
- **R2-B/G**, candidate filesystem readers: reject non-basename artifact names, symbolic links and resolved escapes. Core asset.import no longer accepts a candidate source path; staging reader fixes remain under their exclusive paths.

## Verification

- `python3 tools/check_package_metadata.py`: passed (41 packages, 88 assemblies).
- `python3 tools/check_game_core_csharp.py`: passed: 1,081 C# files; `git diff --check` passed.
- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj`: 99 passed, 2 failed (`CommittedSchemasAreCurrent`: catalog schema requires R2-H; `DiagnosticCodeRegistryIsComplete`: exact 15-code fixture needs the four new stable refusal codes).
- Hollowmere full EditMode run on code commit `c6312eab`, through `bash studio/tools/unity-batch.sh`, one host-wide slot: **124 passed, 0 failed/skipped/inconclusive**, Unity exit 0. XML: `.unity-logs/r2-a-verified.xml`, test duration 198.498 s, Editor run 260 s.
  - `GameCore.Studio.Core.Editor.Tests`: 66 passed.
  - `GameCore.Studio.UI.Editor.Tests` (P2.1): 31 passed.
  - `Hollowmere.P2_1.EditMode.Tests`: 3 passed.
  - `GameCore.Studio.Views.Editor.Tests` (P2.3): 14 passed.
  - `GameCore.Studio.Views.Hollowmere.Tests` (P2.3): 10 passed.
- Final recovery-checkpoint ordering adjustment plus its new regression: focused `HistoryTests|R2CoreRegressionTests` **36 passed, 0 failed/skipped/inconclusive**, Unity exit 0. XML `.unity-logs/r2-a-checkpoint.xml`, test duration 15.443 s, Editor run 60 s.
- XML SHA256: broad `0bb0107b7cf40c20fb49803880a6951303995c1d415c72ce096dece44ee1bb94`; focused `9adcd56884e882f72d4ba6fac3a03f2e17b34c3e41adf2af71b201bf9dcc5f7f`.
- Earlier failing Unity runs exposed test-fixture omissions (artifact manifest and a required fixture.fail argument), both corrected before the passing full run. No existing suite failure was suppressed.

## Left open

- Generated catalog schema is exclusively R2-H's file, and the dotnet exact-registry fixture is outside this packet's test paths. These two tests cannot pass in this isolated branch until the exact requested expectation updates land.
- WebP/glTF/GLB are refused rather than enabling an unverified custom importer. This host's supported built-in importer policy allows PNG/JPEG, WAV/OGG/MP3, TXT/JSON/CSV and FBX only. No raw prefab/controller/material or executable type is admitted.
- D3 Docker/licence qualification belongs to the stage lane packets. R2-A has no stage child launcher and did not claim a confined staging verdict or run paid/node operations.
- Extension operations must call `EditContext.PrepareInverse` before additional filesystem mutations. Generic reflected target-member preimages do not infer arbitrary plugin side effects. The stage/extension contract must require authors to use this protocol.
- Actual OS kill/domain reload acceptance belongs to R2-H. Core tests use deterministic fault injection and fresh StudioRuntime reconstruction; that is not a claim of a killed Editor acceptance run.

## R2-A-int

The dotnet model project compiles `Packages/com.gamecore.studio.core/Runtime/Model/**/*.cs` directly via its
`Compile Include`; there are no copied or symlinked model sources to synchronize. `ToolEntry.ReadOnly` and
`RuntimeOnly` already exist with false defaults. Regenerated all six schemas using
`python3 tools/studio/emit_studio_schemas.py`; only `tool-catalog.schema.json` changed, adding both optional booleans.

### R2 fixes

- R2-02/07/08: `DiagnosticCodeRegistryIsComplete` now asserts the exact 19-code registry, including
  `MediaTypeForbidden`, `MediaPathForbidden`, `MediaImporterInvalid`, and `ArtifactSourceForbidden`.
- R2-06/33: regenerated catalog schema; regression test `CommittedSchemasAreCurrent`.
- Both named tests failed before these changes (2/2) and pass in the full model suite afterward (101/101).
  This supersedes the two model-suite blockers recorded above.

### Verification

- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests`: 101 passed, 0 failed, 0 skipped.
- `dotnet test dotnet/GameCore.sln`: 20 suites, 1,796 passed, 1 failed, 5 skipped (1,802 total), exit 1.
  The sole failure is the out-of-scope gameplay attribute parity test below; the five ETOS live tests require
  `GAMECORE_ETOS_LIVE=1` and were not enabled under the no-paid/node-operations rule.
- `python3 tools/studio/emit_studio_schemas.py --check`: all six schemas current.
- `python3 tools/check_package_metadata.py`: passed (41 packages, 88 assemblies).
- `python3 tools/check_game_core_csharp.py`: passed (1,081 files); `git diff --check`: passed.
- Host evidence: `/tmp/r2-a-int-before/r2-a-int-before.trx`,
  `/tmp/r2-a-int-model/r2-a-int-model.trx`, `/tmp/r2-a-int-solution/*.trx`,
  `/tmp/r2-a-int-solution.log`. Counts verified from TRX results. No Unity or Rust source changed or run.

### Requests to other packets

- **Integrator / gameplay contracts owner**: in
  `Packages/com.gamecore.gameplay.contracts/Runtime/AuthoringMetadata.cs`, add
  `public bool ReadOnly { get; set; }` and `public bool RuntimeOnly { get; set; }` to
  `AuthorOperationAttribute`, both defaulting to false, matching the Studio attribute. Required by
  `GameplayContractsTests.MirrorAttribute_HasTheStudioShape` for the AuthorOperationAttribute pair.
- **R2-F / companion schema owner**: synchronize `studio/agent/schemas/tool-catalog.schema.json` from
  `docs/studio/schemas/tool-catalog.schema.json`; mirror optional boolean `readOnly`/`runtimeOnly` with false
  defaults in `studio/agent/src/model.rs` and accept the four diagnostic codes above. No companion file was edited.
- **R2-H docs**: reconcile the historical 99/101 model result in
  `docs/studio/packets/P0.3-studio-model.md` with this integration result. This micro-packet permits only the
  root packet appendix and its explicit code/schema paths, so that shared note remains unchanged.

### Left open

- Whole-solution green is blocked by the gameplay `AuthorOperationAttribute` parity failure named above;
  the required source is outside this micro-packet's exclusive paths. No test or contract was weakened.

## R2-int1

Integrated on Linux in this `codex/r2-a` clone with `origin/main` at `d0f08805`.
Merge commit `8cf4725d` preserves both packet-note sections in P1.6 and P2.4.

- `ChangeSetEngine`: retained durable prepare/inverse checkpoints, failed-operation preimages,
  media-only imports, apply-time catalog/baseVersions checks and runtime-only live dispatch;
  retained P1.7b's successful-operation list and post-StopAssetEditing Rewitness without adding
  an operation twice to the rollback list. Pure tools do not prepare mutation inverses.
- `ReflectedTool`: ReadOnly comes from the exported entry, populated from AuthorOperation;
  pure calls capture no preimage/touched target; returned authored objects remain touched.
  Attribute readers retain Structural and both ReadOnly/RuntimeOnly. Removed the duplicate
  automatically merged ReadOnly declaration; added gameplay-contract RuntimeOnly mirror parity.
- One core `Editor/Journal/IHistoryEntryHandler` remains. Admission registers on every runtime
  creation and reconfiguration through `runtime.History.RegisterHandler(...)`. All four history
  actions dispatch to the durable admission lifecycle. Finished updates redo bookkeeping only
  after verified completion; Pending retains the existing stack. Tests cover real HistoryService
  undo/default redo, deferred compile completion, and reconfigured recovery with exact preimages.
  R2-B no longer exposes generic mutation tools: its durable pre-effect checkpoint owns package
  installation/removal rather than replaying through EditContext.
- One C# `GameCore.Studio.Authoring.Agent.StageCandidateRequest` retains local project context
  and R2-B's artifact digest/stage-input binding fields. The full constructor validates data paths
  and restores persisted requests. R2-B service docs now use that type and explicitly distinguish
  local verification DTOs from R2-F's HTTP shapes; R2-D still owns the authenticated adapter.
- Stage/admission uses core SecretRedactor, including compiler Errors as well as Detail. No
  second Stage redactor was added. Successful admission rollback reports a successful history result.
- Regenerated the authoritative six schemas and copied all six byte-identically into the companion.
  Rust metadata projections retain readOnly/runtimeOnly/structural with false defaults. New tests
  validate the committed .NET sample catalog, current flag-bearing catalogs and invalid flag types,
  plus byte equality of all embedded schemas. Raw catalog JSON remains authoritative for hashing.

### Verification

- `cargo fmt --check`; `cargo clippy --all-targets -- -D warnings`: pass.
- `cargo test`: **105 passed, 0 failed, 5 ignored** (80 unit, 21 fake-node, 4 stage-lane).
  Ignored: Docker isolation qualification, two real-node tests, two real-Unity stage tests.
- `dotnet test dotnet/GameCore.sln`: **20 suites, 1,810 passed, 0 failed, 5 skipped** (1,815 total).
  Studio.Model 104/104; gameplay attribute parity now passes. Skips are the five credential-gated
  ETOS live tests. TRX evidence: `/tmp/r2-int1-dotnet/*.trx`, log `/tmp/r2-int1-dotnet.log`.
- First unfiltered Hollowmere EditMode XML: 304 total, 298 passed, 1 failed, 5 skipped,
  0 inconclusive. The sole failure was RelationshipsModelTests.TwoThousandNodes_RenderWithinTheFrameBudget:
  SetGraph 18.3464 ms against an unchanged 16 ms threshold during concurrent host workflows.
  XML `.unity-logs/r2-int1.xml`; retained as failed evidence, never counted as a passing run.
- Fresh **unfiltered** Hollowmere EditMode rerun: **304 total, 299 passed, 0 failed, 5 skipped,
  0 inconclusive**, Editor exit 0. XML `.unity-logs/r2-int1-rerun.xml`, 78.704 s test duration.
  The original 16 ms performance threshold is unchanged. Core 67/67, UI 31/31, Views 14/14,
  Views.Hollowmere 10/10, P1.7b 28/28, P2.4 8/8, R2-B 60/60. Studio P2.2 15 passed + four
  credential-gated live skips; the fifth skip is P1.5's graphics-only preview under -nographics.
  Both full runs used `GC_STUDIO_UNITY_SLOTS=1 bash studio/tools/unity-batch.sh` and XML was read.
  Rerun XML SHA256: `da0af69dc3342dae6291547c3f4ba25e39fdaea5819dc6ea7ee9865b1a7c296d`.
  First (failed) XML SHA256: `80d830135c4214daf7c0e1823aff18d4efe7eb678dec90a120b34d9c9d70b8c3`.
- `python3 tools/studio/emit_studio_schemas.py --check`: all six current.
- `python3 tools/check_package_metadata.py`: 41 packages, 89 assemblies, pass.
- `python3 tools/check_game_core_csharp.py`: 1,095 C# files, pass.
- `cargo build --release --locked`: pass. Generated migration-test asset reserialization was
  restored after Editor exit; no sample content changes are included in this integration.

### Installed companion

**Reinstall skipped: live-run guard remained busy at the end of the 45-minute window.**
Polling began `2026-10-05T07:02:53Z` every 60 seconds; final confirmation was `2026-10-05T07:48:13.899734+00:00`.
A brief empty poll at 42 minutes was followed by a nonempty immediate pre-install recheck;
the guarded launcher exited 75 before invoking install.sh. No service restart or installation
mutation occurred. The release build is ready, but the installed companion has **not** received
this branch's schema fix. See the exact guarded commands and redacted status in
`docs/studio/packets/P0.5-companion.md` §R2-int1. Guard evidence: `/tmp/r2-int1-live-gate.json`.


Historical packet restrictions and isolated-branch blockers above are superseded only by this
integration's explicitly authorized work and measured results. No passing Docker Unity staging,
paid provider workflow, or complete Stage → Admit transport qualification is claimed here.
