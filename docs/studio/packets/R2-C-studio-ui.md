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
- Walking evidence (`EvidenceContractTests.test_R2_29_WalkingRequiresHudRoutingAndCommittedPose`): dispatch New Game, wait for HUD and settled world, focus the image, measure committed world.posX/posZ. Evidence shell requires the corresponding successful measurement. Graphical run deliberately not invoked.

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

## Verification

Host only; Unity 6000.0.75f1 through `unity-batch.sh`, at most one held Editor slot. Test arguments match
`unity-compile.sh`: `-runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\.UI.*|Hollowmere\.P2_1.*'`;
`--results` supplies the wrapper-owned `-testResults` path (a first invocation repeating that reserved argument
was rejected before launching Unity).

- Fixed implementation at `c5968bb9`: XML `.unity-logs/r2-c-ui-final.xml`: **49 total, 47 passed, 0 failed,
  2 skipped, 0 inconclusive**, test duration 28.166 s; Editor exit 0. The wrapper returns partial/exit 1 because
  graphical skips are not acceptance passes. UI: 44 passed + 2 skipped; Hollowmere P2_1: 3 passed.
- Explicit skips: `R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers` (no graphics device to create
  an event-delivering window) and `Render_TargetMatchesTheViewportArea`. Both name R2-H graphical qualification.
  Headless ownership, key-state reset and low-level suppression tests pass.
- Before/after proof: restored the original Editor implementation from parent `de2d9593`, keeping only an identity
  `StudioStyles.Safe` shim so the new evidence caller compiles. Current regression tests and their honest graphics
  skip declarations remained unchanged. `.unity-logs/r2-c-before.xml`: **16 total, 13 failed, 1 passed, 2 skipped**.
  The sole pass is the host-without-opt-in refusal negative control. Every implementation file was restored from
  byte backups and `git diff --exit-code -- Packages/com.gamecore.studio.ui/Editor` passed afterward.
- `python3 Packages/com.gamecore.studio.ui/Tests/Host/test_evidence_contract.py`: **1 passed** (seven cases);
  tests only the evidence report footer, never launches Unity/ETOS. Running it against the parent evidence script
  failed all seven assertions. Host evidence: `/tmp/r2-c-before-evidence-test.log`.
- `python3 tools/check_game_core_csharp.py`: pass, 1,098 C# files.
- `python3 tools/check_package_metadata.py`: pass, 41 packages / 89 package assemblies, exact dependencies.
- `bash -n studio/tools/evidence-p2.1.sh` and `git diff --check`: pass.
- Initial cold compile stopped on two incorrect test selection-ID calls and a missing evidence assembly reference;
  corrected without touching other packets. First executable suite: 46 passed / 2 input-test failures / 1 graphics
  skip. Those failures established that queued keyboard input and UI Toolkit window delivery are unavailable under
  this headless runner. The headless test now directly checks state/reset/event handling; the window test explicitly
  skips without a graphics device. No product failure was suppressed.

Final restored-source run at `c5968bb9`, `.unity-logs/r2-c-restored.xml`: **49 total, 47 passed, 0 failed, 2 skipped, 0 inconclusive**; test duration 12.391609 s, Editor exit 0, 296 s host run. The same two graphical tests are explicitly skipped. All restored implementation bytes match the committed sources.

XML SHA-256:

- `r2-c-ui-final.xml`: `aabaefc52dd328f8bd9f796bcc628e063047eaac37557e2b11c4c909cd04059d`.
- `r2-c-before.xml`: `c534a414125f4a24b2647eb365d1d6e5aa78bc3daa6982d5867e8af753eab0d9`.
- `r2-c-restored.xml`: `9dda6e02abfa3646b5c6af42f6bb9f03a967f32d660e358967e865746089a0b0`.

## Requests to other packets

- **R2-D** `Packages/com.gamecore.studio.etos/Editor/EtosAgentGateway.cs` / session setup: configure `StageAdmission.Of(runtime).Options.StageService` (`IStageService`), ProjectId, SourceRevision and CatalogRevision on every reload from trusted project context. Set `AllowHostConfinement` only from authenticated hello/operator configuration, never candidate JSON. UI uses `RequestStage(StageCandidateRequest)` then `GetVerdict(jobId)` through `FetchVerdict`; `GetVerdict` must await terminal completion, return the authenticated signed record, and surface failed jobs with redacted reasons. Allow cold jobs their companion deadline. UI does not serialize the local DTO onto HTTP. `VerifyVerdict` must send the exact signed record as required by R2-B/F.
- **R2-A** `Editor/Index/SemanticIndexService.cs`, `Slice(selection, depth, byteCap, includeTypes)`: bound traversal and per-node serialization work before allocating the full closure; enforce actual envelope bytes in core. R2-C enforces the returned slice byte cap and limits selected roots but cannot change core traversal in its exclusive paths.
- **R2-H** run corrected `studio/tools/evidence-p2.1.sh` graphically and qualify image focus/movement/minimum rendered area on the integrated revision. Required walk record is `name:walk-measurement`, `hud:true`, `routing:true`, `source:world.posX/posZ`, positive measured seconds and displacement >0.05 m. Rendering skips are not acceptance passes.

## Left open

- Production Stage → Admit transport qualification requires R2-D's authenticated adapter, absent on this baseline. UI fails closed without it. Deterministic service tests do not qualify real HMAC transport or package admission.
- No candidate stage child is launched by this UI packet. Existing R2-F/G Docker evidence reports Unity exit 198 (no valid headless entitlement); no host fallback or passing sandbox verdict is claimed. Trusted EditMode tests on the host are not candidate sandbox evidence.
- Graphical evidence explicitly belongs to R2-H and was not run. Actual rendered layout/focus/keyboard-to-game acceptance remains for that run.
