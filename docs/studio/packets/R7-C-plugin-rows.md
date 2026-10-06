# R7-C plugin rows — PACKET

Branch: `omp/r7-c`. Scope: W-PLUG-01/02/03/08/10/11 and W-GAME-05. No paid operations, installed companion changes, etosd changes, or sibling-clone changes.

**Outcome: six rows PASS; W-PLUG-10 FAIL with demonstrated out-of-scope Studio defects.** Passed: W-PLUG-01/02/03/08/11 and W-GAME-05. Integrated matrix after only these seven row updates: **41 PASS / 23 BLOCKED / 4 FAIL (68 rows)**. No scenario or budget wording changed.

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

`fc3245ea` enrolls the 28 authored voices and adds actual native dialogue playback proof. The final Linux IL2CPP build and full recording identify this product revision. The final test-only change makes the native voice case Explicit for device-less suites; selecting its full name in a graphics-enabled batch Editor yields 1/1 Passed, not Skip.

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
- Final metadata checker: 42 packages, 91 package assemblies; C# checker: **1,240 files**, both pass. A 120-second checker timeout during the build is retained; final combined invocation completes in 66.39 seconds.
- Distinct passing Unity case names across the final 5-case regional, 23-case existing EditMode, 20-case existing PlayMode and 1-case explicit voice XML: **49**. Separately, the real parity case fails; repeated before/intermediate attempts are not added to the distinct pass count.
- Final Linux IL2CPP build: **PASS**, revision `fc3245ea4143220629a1a562081a2d097da39759`, 0 errors/4 warnings, 163-second wrapper duration; build smoke exits 0. Final standalone capture **PASS**: 350.2 seconds at 1080p, actual UI save/quit, distinct second process, matching restored state, Ending C, fresh restart, both exit 0. Initial 386.2-second recording remains retained.
- Final actual monitor waveform identifies Maren greeting (normalized correlation **0.5905**), village (**0.4642**) and marsh (**0.4552**), with wrong-region controls **0.0492/0.0740**. Source WAV/video hashes and the runnable analysis script are in W-PLUG-11. Actual sources overlap with positive gains during the full regional probe. This is measured audible content, not an unperformed human-listening claim.
- Both movies and selected actual menu/save/ending/restart frames were visually inspected. Some llvmpipe scene materials are magenta; no rendering-quality or B-FRAME qualification is claimed. Build-generated URP serialization noise is preserved as an evidence diff and reverted from source; unrelated autogenerated metadata/bytecode is removed.

## Left open

- **W-PLUG-10 cannot pass inside R7-C's exclusive paths.** Real XML demonstrates missing candidate definition validation and inspector location loss; exact Studio core/UI requests above remain actionable for their owners. This is reported as FAIL, not a prerequisite-only BLOCKED or fabricated pass.
- The excluded media authoring generator still needs the voice-bank enrollment seam for *future* generated clips. The current 28 authored clips play in the final qualified player.
- Full native snapshots remain on this Linux host at the SHA-256 manifest paths, not in Git as multi-gigabyte blobs. First-run raw captures were verified against their retained gzip archives before redundant raw copies were removed. Other raw captures remain intact.
- No paid operations, installed companion/etosd restart, other packet's clone, real-GPU performance rerun, CORE-PICK probe or unrelated matrix row was touched.
