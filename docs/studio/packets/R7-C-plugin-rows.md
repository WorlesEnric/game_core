# R7-C plugin rows — PACKET

Branch: `omp/r7-c`. Scope: W-PLUG-01/02/03/08/10/11 and W-GAME-05. No paid operations, installed companion changes, etosd changes, or sibling-clone changes.

## R2 fixes

R2-38: exact acceptance drivers are added for actual Animator evaluation/respawn, deterministic ledge clearance/landing, repeated barn lantern interaction across production save/restore, native regional media measurements, and diagnostic parity through the real inspector, validator console and fake-transport ETOS candidate pipeline. Evidence, not the presence of drivers, determines row disposition.

| Row / finding | Fix or exact driver | Retained test |
|---|---|---|
| W-PLUG-01 / R2-38 | Wait for unused native scene assets after unload; measure each region's exclusive textures and production-bank ambience; move actual Odd and require zero departed views | `W_PLUG_01_TravelReleasesRegionNativeMediaWithinFivePercentPeak` |
| W-PLUG-02 / R2-38 | Actual controller state and animated-pose evaluation through production binders, variant swaps, despawn and respawn | `W_PLUG_02_AnimatorEvaluatesCommittedVariant_AndRespawnRetainsOverrides` |
| W-PLUG-03 / R2-38 | Walking-only negative control, airborne clearance of a solid ledge, descent/contact and identical two-boot motion traces | `WPLUG03_DeterministicJumpClearsSolidLedgeAndLands` |
| W-PLUG-08 / R2-38 | 32 actual barn interactions around cooldown and production save/restore; exactly one inventory lantern | `R7C_WPLUG08_RepeatedBarnLanternPickupAndReload_LeavesExactlyOneLantern` |
| W-PLUG-10 / R2-38 | Actual inspector, validator console and fake-transport/live-gateway candidate staging expose two missing Studio seams | `WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts` (FAIL; requests below) |
| W-PLUG-11 / R2-38 | Finished fades detach outgoing source; region ambience releases unshared native data and reloads on return; paused/shared source references retain their lease | `W_PLUG_11_CompletedFadeReleasesOutgoingSourceClip`, `W_PLUG_11_SharedClipLeasePreventsUnloadingAndUnsharedClipReleasesData` |
| W-GAME-05 / R2-38 | Production UI/autoplay two-process save/quit/relaunch/load/ending/restart driver; read-only state audit | `run_lifecycle.py`, `RestoreAssertionRejectsFreshWorldOrMissingQuestItem` (the latter is not player acceptance) |

Code checkpoints: `7ad4a809` adds drivers; `7da2148a` fixes measured native lifetimes. The source-release test fails before the fix and passes after. The texture probe first retained 100%; after texture cleanup, region audio retained 55.58–100%; native data release reduces final residuals to 0.08825–0.15878%, including the 702-byte Unity clip descriptor. No descriptor is silently subtracted from the budget.

## Requests to other packets

W-PLUG-10 requires Studio-owned seams outside this packet's exclusive paths:

- `Packages/com.gamecore.studio.ui/Editor/Context/ContextPanelView.cs`, `ValidatorDiagnostics.For(UnityEngine.Object target)`: retain `GameplayDiagnostic.SubjectId` as canonical `Diagnostic.Where`, as the validator console already does. If resolution needs a runtime, add `StudioRuntime runtime` and migrate both inspector callers.
- `Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs`, `StageCore(ChangeSet changeSet, StageOptions options, bool allowInternal, bool validate)`: run gameplay definition validation on affected/proposed definition state and preserve canonical code, message and subject location in candidate diagnostics. Missing-prefab `EntityDefinition` must refuse with `GP-ENT-006` through the live-agent pipeline, not only inspector/console. A gameplay adapter cannot override built-in `set`/`assign`, and no generic definition-validator service registration exists.

- `games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereMedia.cs`, `AttachGenerated(StudioAuthor a)`: after attaching generated clips to dialogue nodes, also journal `audio.assignClip` into `HollowmereAudioBank` with `clipId = AudioClip.name`, `group = Voice`, volume 1, loop/spatial false. This excluded Authoring path currently binds graph references but never enrolls voice playback. R7-C implements the content side: all 28 existing voiced assets are now enrolled in the owned bank asset; ordinary SFX authoring only upserts SFX and does not delete these entries. Future generated voices need the authoring-side seam to avoid recurrence.

## Verification

Every Editor uses `studio/tools/unity-batch.sh` and at most one Editor is held by this packet. The scoped `Tests/R7_C/batch-graphics.py` adapter removes only `-nographics` and refuses non-batch launches; allocator, redactor, timeout and XML handling remain in the existing wrapper. Xvfb rendering is not a real-GPU B-FRAME claim.

- Gameplay rules TRX: **309/309 passed**, no skips.
- Initial executed EditMode XML: Animator and lifecycle-state regression pass; outgoing source retention and real diagnostic parity fail. Earlier harness compilation failures (Unity NUnit compatibility and missing explicit assembly references) are retained separately, never counted as product-test outcomes.
- Regional memory first passing XML: **3/3**; complete repeat with moved Odd/zero views and production reload: **5/5**, no skips. Six Memory Profiler snapshots per run are host-retained with exact paths/hashes, following W-GAME-08's manifest convention.
- PlayMode: initial **13 pass / 1 fail**; the lantern driver initially over-constrained interaction success rather than inventory grants. Corrected exact one-lantern contract: **10/10** including all FullQuestHeadless cases and two-boot ledge proof. The separate walking/focus/dispatch/travel and UI/audio boot cases pass in the first run.
- Final existing world/audio EditMode suites: **23/23 passed**. First launcher exited 1 after a transient ILPP fault despite passing XML; clean repeat exits 0 with the same 23 passes. Final existing PlayMode suites: **20/20 passed**, including moved-NPC/save/restore cases, full quest, real walking and ledge.
- W-PLUG-11 actual Maren dialogue regression fails before bank enrollment (`Voice.clip == null`) and passes after the 28 authored voice clips are enrolled. Post-fix FullQuestHeadless XML: **10/10 passed**. The initial movie had audible ambience but not the missing voice; it is not promoted as voice evidence.
- Metadata checker: 42 packages, 91 package assemblies; C# checker: 1,239 files, both pass.
- Current standalone build/capture results are recorded with the W-GAME-05 row, not inferred from Editor tests.

## Left open

No row is promoted before its XML/assertion/capture evidence exists. Inspector/candidate parity requires the two out-of-scope production changes above.
