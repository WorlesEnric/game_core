# P3.1b — Hollowmere frame-time follow-ups

Branch: `codex/p3.1b`. Host: myubuntu. `git fetch origin && git merge origin/main` reported already up to date.
Exclusive edits: games/hollowmere, SaveService.cs (necessary capture/write seam), P3.1b evidence, P3.1 packet appendix.

## R2 fixes

These follow-ups are the P3.1 B-FRAME observations, not newly numbered R2 review findings.

- P31b-SAVE: keep capture, delivery ownership, serialization and canonical slot hashing on the main thread; write only the immutable snapshot on a worker. `CaptureSnapshot`, `WriteCapturedAsync`, `CaptureAsync` share the original capture path. Header rename commits a content-addressed checkpoint; legacy slot.gcc remains readable. Keep current and previous generations, collect older generations after commit. Refuse overlapping save/load/delete. The game supplies the public BindingHost callback (including mouse/controller activation and autoplay); show “Saving…” until completion, then the saved HUD message. Tests: `P31b_SAVE_AsyncWriteRestoresCapturedHashAndConfirmsAfterWrite`, `P31b_SAVE_InterruptedCaptureAndFailedHeaderKeepPreviousSave`. Before: the game command immediately blocks on file writes, and a failed header publication can make the old checkpoint unreadable.
- P31b-BELFRY: enable streamer neighbour preloading while the player has the clapper in the marsh, before the ferry conversation. Disable on departure; no custom residency writes or extra pumps. Tests: `P31b_BELFRY_PreloadIsLimitedToFerryPreparation`, `P31b_BELFRY_PreloadedRegionReallyUnloadsAndReloads`. Before: belfry is unloaded until travel commits.
- P31b-BOOT: defer first-region IO for three menu presentation frames, with low-priority background asset integration; retain real scene loader reconciliation and restoration. Test: `P31b_BOOT_FirstRegionWaitsForMenuFramesWithoutFakingResidency`. Before: loader begins IO immediately. The B-FRAME window is unchanged: 07 has no boot exemption.

## Verification

Pending host runs; XML is authoritative. Evidence under artifacts/studio/evidence/P3.1b.
The existing video (20,223,247 bytes; 636.533 seconds container duration) and keyframes predate these fixes. No recording is redone.

## Requests to other packets

None currently. The shared gameplay UI runtime remains synchronous for its generic callers; Hollowmere supplies its own command callback through the existing BindingHost surface.

## Left open

- The P3.1 note names an xvfb headless rehearsal but commits no distinct frame-time probe executable or command transcript for it. This packet will retain its player, autoplay script, FrameLogRecorder and exact statistics calculation; command/environment differences will be recorded with the new evidence.
- Budget outcomes are pending measurement. The first ready frame still includes boot; it is not suppressed or relabelled.

### Host qualification checkpoint

- Full Hollowmere EditMode XML: **446 passed, 0 failed, 12 skipped** (458). The baseline 392 passes is exceeded on merged main. Skips: seven live ETOS/workflow tests, four graphics-only tests, one explicit memory-cycle test. No paid/live tests enabled.
- Full Hollowmere PlayMode XML: **22/22 passed** (P3.1's 19 plus three regressions). New manual-save capture measured 47.159 ms in this run. Both new save tests and real belfry unload/reload pass.
- dotnet Execution: **178/178 passed**. Required metadata and C# checks pass.
- The suite's rebake exposed merged main's new cosmetic `EntityDefinition.materialTextures` field. Keep the 28 refreshed definition stamps and two bake outputs: their only definition changes are empty `materialTextures` plus content stamps; structural stamps, catalog fingerprint and recipe revisions remain unchanged. These are needed for Verify on the delivered checkout, rather than discarding them as transient test edits.
- Isolated `P31AuthoringTests.BakeVerifies`: **1/1 passed** after committing the refreshed bake. This checks the delivered assets without a preceding authoring fixture mutating them.
- Existing P1.1 PlayMode region loop: marsh→belfry **9 ms**, 10 frames; full loop 33 frames / 33 sanctioned pumps / zero violations. This is a suite diagnostic, not the required player measurement.
