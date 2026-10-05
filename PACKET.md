# R2-A — core edit engine, recovery and artifact authority

Branch: `codex/r2-a`. Build host: myubuntu. Scope: studio.core excluding Editor/Stage; core package tests. No sibling clone, companion service, ETOS node operation or credential file was accessed.

## R2 fixes

| Finding | Implementation | Regression test in `R2CoreRegressionTests` |
|---|---|---|
| R2-01 | Hidden unsaved preview transform; begin-drag stamp retained; only engine commit changes target | `R2_01_SaveAndReloadDuringDragPreserveRealTransform` |
| R2-02 | Shared importer-settings check before writes; retain bytes/meta before import; failed results retain inverse | `R2_02_InvalidImporterLeavesExistingBytesAndMetaUnchanged` |
| R2-03 | `EditContext.PrepareInverse(Operation[], bool)` durably checkpoints preimages before writes; file-write fault hook; history transition checkpoint before inverse work | `R2_03_CrashInsideFileWriteHasDurablePreimage`, `R2_03_UndoCrashCheckpointsBeforeInverseMutation` |
| R2-04 | Per-inverse recovery checkpoints; failures preserve Interrupted and inverse evidence; resume distinguishes successful dependencies; original AllOrNothing preimages rolled back on resume failure | `R2_04_FailedInverseRemainsInterruptedWithEvidence`, `R2_04_ResumeDoesNotDropFailedDependency` |
| R2-05 | Apply checks staged catalog revision and every baseVersions read | `R2_05_ReadDependencyChangedAfterStageRefusesApply`, `R2_05_CatalogChangedAfterStageRefusesApply` |
| R2-06 | Translated live actions do not invoke authored tool; non-undoable runtime journal entries; atomic runtime batches refused; RuntimeOnly catalog metadata validates mixed batches | `R2_06_RuntimeActionsDoNotWriteAuthoredDataAndCannotUndo`; existing `LiveEditTests` updated to D4 |
| R2-07 | Default-deny raw-byte media allowlist shared by import/bind; Editor/Plugins and linked destinations refused | `R2_07_ExecutableAndHookPathsRefusedBeforeWrite` (10 cases) |
| R2-08 core | Artifact digests only; source/sha256 host-file arguments removed from catalog and never read | `R2_08_SourcePathNeverReadAndAbsentFromCatalog` |
| R2-13 core | Typed `StageCandidateRequest` / `ICandidateStageGateway` seam | Consumer integration requested below |
| R2-15 core | HistoryService dispatches Undo/Redo/Resume/Rollback to per-runtime IHistoryEntryHandler keyed by HistoryEntryKind | `R2_15_AllHistoryActionsUseTypedAdmissionHandler` |
| R2-22 core | Shared SecretRedactor covers all ETOS prefixes, sk-, bearer, recursive JSON secret keys; RedactingTextWriter buffers split tokens before sink writes | `R2_22_SharedRedactorMasksNestedJsonAndSplitChildOutput` |
| R2-33 core | ReadOnly model/mirror metadata flows to ToolEntry and ReflectedTool | `R2_33_ReadOnlyMetadataFlowsThroughRegistry` |
| R2-36 core | Default NestedReferenceContributor installed in StudioRuntime; nested refs and field labels in node projection; canonical endpoint resolution | `R2_36_CoreReferencesInstalledBeforeAnyView` |
| R2-39 core | Production TypeCache discovery excludes test/fixture/NUnit assemblies; tests inject sources explicitly | `R2_39_ProductionDiscoveryExcludesFixtures` |

## Contracts and requests to other packets

- **R2-B**, `Editor/Stage/StageAdmission.cs`: implement `IHistoryEntryHandler` (`Kind => HistoryEntryKind.Admission`, `HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force)`) and register through `runtime.History.RegisterHandler(handler)` on every runtime rebuild. All admission transitions must retain their durable compile/reload state. Core refuses admission history when no handler is installed. Internal admit/remove tools must call `EditContext.PrepareInverse` before package side effects. C/E must call HistoryService, without special-casing admission.
- **R2-C/D/F**, UI CandidateCoordinator / ETOS gateway / companion stage route: consume `GameCore.Studio.Authoring.Agent.StageCandidateRequest(changeSetId, projectId, projectPath, repoRoot, sourceRevision, catalogRevision)` through `ICandidateStageGateway.StageCandidateAsync(request, cancellationToken)`. Project paths must come from operator configuration, never candidate JSON. `FetchTrustedVerdictAsync(jobId, cancellationToken)` must use authenticated `/v1/stage/{job}/verdict`; retain returned signed record and verdict reference. No core consumer authenticates arbitrary artifact bytes.
- **R2-G**, `com.gamecore.gameplay.world/Editor/WorldTools.cs`: register production `ILiveOpTranslator` through `runtime.Services.RegisterLiveTranslator(translator)` after runtime creation/reload (unregister supported); `CanTranslate(EditContext)` identifies runtime actions and `CompositionEditPayload? Translate(EditContext, out Diagnostic?)` returns the validated world edit. Mark action metadata `RuntimeApplicability = RuntimeApply.Live, RuntimeOnly = true` in shared/mirror attributes. Core records world operation IDs, never calls the authored tool for translated actions, and refuses multi-op AllOrNothing live batches without an atomic world gateway. Apply-to-authored must be a separate persistent change set.
- **R2-E**, Views `NestedReferenceContributor.cs` / `StudioViewContext.cs`: remove the Views contributor and registration/rebuild side effect; consume `runtime.References` (`GameCore.Studio.Edit.NestedReferenceContributor`) and node `Refs` field labels. Opening a view must not change core semantic coverage. Mark pure tools ReadOnly in shared/mirror metadata, and call `runtime.Registry.Invoke(toolId, target, args)`.
- **R2-D/F/G**, errors/log capture/evidence: use `SecretRedactor.Redact(string)`, `RedactJson(JToken)` and `RedactingTextWriter(TextWriter)` at sinks, without retaining raw inner transport exceptions. Flush keeps an incomplete line private until newline/disposal to prevent token fragments from leaking.
- **Integrator**, `dotnet/tests/GameCore.Studio.Model.Tests/JsonRoundTripTests.cs`, `DiagnosticCodeRegistryIsComplete`: extend the exact expected registry with `MediaTypeForbidden`, `MediaPathForbidden`, `MediaImporterInvalid`, `ArtifactSourceForbidden`. This test file is outside R2-A exclusive paths. Companion code-registry mirrors should accept the same four codes.
- **R2-C**, `Editor/Gizmo/ViewportMoveGizmo.cs`: while dragging, project `GizmoMoveController.PreviewTransform.position` for the handle origin instead of the real target transform; core Scene-view EditorTool already consumes this preview seam.
- **R2-H**, `docs/studio/schemas/tool-catalog.schema.json`: regenerate schemas from the updated core ToolEntry (optional boolean `readOnly` and `runtimeOnly`, default false). File is outside R2-A ownership. Corresponding mirror changes are required in `studio/agent/src/model.rs` (R2-F) and gameplay attribute declarations (owner/integrator). Existing catalog entries serialize identically when both flags are false.
- **R2-B/G**, candidate filesystem readers: reject non-basename artifact names, symbolic links and resolved escapes. Core asset.import no longer accepts a candidate source path; staging reader fixes remain under their exclusive paths.

## Verification

- `python3 tools/check_package_metadata.py`: passed (41 packages, 88 assemblies).
- `python3 tools/check_game_core_csharp.py`: passed at initial implementation checkpoint; final rerun pending.
- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj`: 99 passed, 2 failed (`CommittedSchemasAreCurrent`: catalog schema requires R2-H; `DiagnosticCodeRegistryIsComplete`: exact 15-code fixture needs the four new stable refusal codes).
- Hollowmere EditMode core/UI/Views run through `bash studio/tools/unity-batch.sh`, under one host-wide slot: running. Final XML counts pending.

## Left open

- Generated catalog schema is exclusively R2-H's file, and the dotnet exact-registry fixture is outside this packet's test paths. These two tests cannot pass in this isolated branch until the exact requested expectation updates land.
- WebP/glTF/GLB are refused rather than enabling an unverified custom importer. This host's supported built-in importer policy allows PNG/JPEG, WAV/OGG/MP3, TXT/JSON/CSV and FBX only. No raw prefab/controller/material or executable type is admitted.
- D3 Docker/licence qualification belongs to the stage lane packets. R2-A has no stage child launcher and did not claim a confined staging verdict or run paid/node operations.
- Actual OS kill/domain reload acceptance belongs to R2-H. Core tests use deterministic fault injection and fresh StudioRuntime reconstruction; that is not a claim of a killed Editor acceptance run.
