# R2-C — Studio UI

Branch `codex/r2-c`; Linux build host myubuntu. `git fetch origin && git merge origin/main` returned already up to date. R2-A's former root note is now `docs/studio/packets/R2-A-core-edit-recovery.md`; its published seams and R2-B's admission contracts are consumed here.

## R2 fixes

- R2-13: coordinator builds `StageCandidateRequest` with `StageAdmission.BuildStageRequest`, submits through configured `IStageService`, retains job/request in project-scoped SessionState, and calls authenticated `FetchVerdict`. Reloaded jobs require Refresh verdict and fresh verification. Both panel and viewport-strip Admit require the in-memory verified verdict, all mandatory checks, and trusted host opt-in. Display includes job, digest, confinement, cold cache, seven steps and forbidden hits. File-verdict admission UI and legacy reflected `packageRef` calls removed.
- R2-15: Undo, Redo, Resume and Rollback all dispatch through HistoryService; no admission special case remains in the panel.
- R2-29: only the image is keyboard focusable for gameplay; prompt, toolbars and overlay controls cannot route keys. Focus/mode/window/Play loss releases routing and resets held devices synchronously. Image-owned game keys consume Editor defaults.
- R2-24: actual compact UTF-8 slice bytes bounded (including envelope); scene context stops at 128 objects/64 KiB, reports truncation, and redacts text. Selection is limited to 128 targets/parts. Up to eight file attachments, 16 MiB aggregate including scene context; budget checked before reads and bounded reads refuse file growth. Prompt shows truncation.
- R2-30/D6: tiled Studio area at least 1280×720; viewport image at least 640×360; window minimum restored on OnEnable.
- R2-37: unavailable graphics/layout rendering explicitly Ignore with graphical-qualification reason, never Assert.Pass.
- R2-40: Reject tooltip says reason is recorded locally.
- R2-01 consumer: gizmo origin and axes project PreviewTransform while dragging, preserving the real target.
- R2-22/D9: shared core SecretRedactor at UI labels, tooltips, output JSON, logs/exceptions, task persistence and evidence writes; imported inspector strings sanitized without committing masks.
- Walking evidence: dispatch New Game, wait for HUD and settled world, focus the image, measure committed world.posX/posZ. Evidence shell requires the corresponding successful measurement. Graphical run deliberately not invoked.

## Tests

`Packages/com.gamecore.studio.ui/Tests/Editor/R2UiRegressionTests.cs`:

- R2-13: `R2_13_TypedRequestFetchesVerifiedVerdictAndDisplaysBinding`, `R2_13_HostAdmissionRequiresTrustedOperatorOptIn`, `R2_13_UnverifiedOrIncompleteVerdictNeverEnablesAdmit`.
- R2-15: `R2_15_AllPanelHistoryActionsUseGenericDispatch`.
- R2-24: `R2_24_ScenePackingStopsAtUtf8ByteCapAndReportsTruncation`, `R2_24_AttachmentCountAndAggregateBytesAreBoundedBeforeRead`.
- R2-29: `R2_29_OnlyImageFocusOwnsGameInputAndLossResetsHeldKeys`, `R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers`.
- R2-30: `R2_30_ReopenedViewportAndLayoutEnforceMinima`.
- R2-01: `R2_01_GizmoProjectsPreviewWhileRealTargetRemainsUnchanged`.
- R2-22/R2-40: `R2_22_40_PanelSinksRedactSecretsAndRejectReasonIsLocal`.
- R2-37: existing `ViewportWindowTests.Render_TargetMatchesTheViewportArea` now has the honest skip disposition.

Verification in progress: C# policy and exact package metadata checks passed; shell syntax and diff whitespace passed. Hollowmere requested filter `GameCore\.Studio\.UI.*|Hollowmere\.P2_1.*` queued through `unity-batch.sh` under the shared host allocator. Result counts will be read from XML.

## Requests to other packets

- **R2-D** `Packages/com.gamecore.studio.etos/Editor/EtosAgentGateway.cs` / session setup: configure `StageAdmission.Of(runtime).Options.StageService` (`IStageService`), ProjectId, SourceRevision and CatalogRevision on every reload from trusted project context. Set `AllowHostConfinement` only from authenticated hello/operator configuration, never candidate JSON. UI uses `RequestStage(StageCandidateRequest)` then `GetVerdict(jobId)` through `FetchVerdict`; `GetVerdict` must await terminal completion, return the authenticated signed record, and surface failed jobs with redacted reasons. Allow cold jobs their companion deadline. UI does not serialize the local DTO onto HTTP. `VerifyVerdict` must send the exact signed record as required by R2-B/F.
- **R2-A** `Editor/Index/SemanticIndexService.cs`, `Slice(selection, depth, byteCap, includeTypes)`: bound traversal and per-node serialization work before allocating the full closure; enforce actual envelope bytes in core. R2-C enforces the returned slice byte cap and limits selected roots but cannot change core traversal in its exclusive paths.
- **R2-H** run corrected `studio/tools/evidence-p2.1.sh` graphically and qualify image focus/movement/minimum rendered area on the integrated revision. Required walk record is `name:walk-measurement`, `hud:true`, `routing:true`, `source:world.posX/posZ`, positive measured seconds and displacement >0.05 m. Rendering skips are not acceptance passes.

## Left open

- Production Stage → Admit transport qualification requires R2-D's authenticated adapter, absent on this baseline. UI fails closed without it. Deterministic service tests do not qualify real HMAC transport or package admission.
- No candidate stage child is launched by this UI packet. Existing R2-F/G Docker evidence reports Unity exit 198 (no valid headless entitlement); no host fallback or passing sandbox verdict is claimed. Trusted EditMode tests on the host are not candidate sandbox evidence.
- Graphical evidence explicitly belongs to R2-H and was not run. Actual rendered layout/focus/keyboard-to-game acceptance remains for that run.
