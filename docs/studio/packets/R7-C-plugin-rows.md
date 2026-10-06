# R7-C plugin rows — PACKET

Branch: `omp/r7-c`. Scope: W-PLUG-01/02/03/08/10/11 and W-GAME-05. No paid operations, installed companion changes, etosd changes, or sibling-clone changes.

## R2 fixes

R2-38: exact acceptance drivers are added for actual Animator evaluation/respawn, deterministic ledge clearance/landing, repeated barn lantern interaction across production save/restore, native regional media measurements, and diagnostic parity through the real inspector, validator console and fake-transport ETOS candidate pipeline. Evidence, not the presence of drivers, determines row disposition.

## Requests to other packets

W-PLUG-10 requires Studio-owned seams outside this packet's exclusive paths:

- `Packages/com.gamecore.studio.ui/Editor/Context/ContextPanelView.cs`, `ValidatorDiagnostics.For(UnityEngine.Object target)`: retain `GameplayDiagnostic.SubjectId` as canonical `Diagnostic.Where`, as the validator console already does. If resolution needs a runtime, add `StudioRuntime runtime` and migrate both inspector callers.
- `Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs`, `StageCore(ChangeSet changeSet, StageOptions options, bool allowInternal, bool validate)`: run gameplay definition validation on affected/proposed definition state and preserve canonical code, message and subject location in candidate diagnostics. Missing-prefab `EntityDefinition` must refuse with `GP-ENT-006` through the live-agent pipeline, not only inspector/console. A gameplay adapter cannot override built-in `set`/`assign`, and no generic definition-validator service registration exists.

## Verification

Qualification results will be recorded after serialized host runs. Every Editor uses `studio/tools/unity-batch.sh` and at most one Editor is held by this packet. The scoped `Tests/R7_C/batch-graphics.py` adapter removes only `-nographics` and refuses non-batch launches; allocator, redactor, timeout and XML handling remain in the existing wrapper. Xvfb rendering is not a real-GPU B-FRAME claim.

## Left open

No row is promoted before its XML/assertion/capture evidence exists. Inspector/candidate parity requires the two out-of-scope production changes above.
