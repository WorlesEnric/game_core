# R9-A — coherent change-set projection

This is the packet's PACKET.md. Branch `omp/r9-a`; Linux build host, 2026-10-07.

## R2 fixes

| Finding / row | Fix | Regression / retained witness |
|---|---|---|
| P4.2i Core projection, W-AI-02 | One detached definition graph resolves created assets by path/identity, applies dependent writes in dependency order, projects trusted graph enrollment, remaps references, then invokes mandatory validators. No asset is created during projection. | `DefinitionProjectionTests.R9A_CreatedReferenceAndDependentSetAssignApplyInDependencyOrderAndUndo`; `R9A_CreatedDefinitionFieldsResolveAnEarlierCreatedReferenceWithoutLiveWrites`; unchanged `W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json`. |
| P4.2i Core projection, W-AI-03 | Project trusted dialogue composition before dependent `set`/`assign`; validate only the final graph. Trusted adapter explicitly enumerates clone-only mutations; asset/scene-producing reflected tools are never invoked by the projector. | `R9A_ReflectedCompositionThenConnectValidatesOnlyTheFinalGraphAndUndoRestores`; unchanged `W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json`. |
| W-VIEW-02 | Owned regression submits AddLine, Connect and Rename in one final-valid journaled change set. Mandatory reachability and dangling-edge validation remain unchanged. | `Hollowmere.R9_A.FinalDialogueWorkflowTests.Dialogue_AddLineConnectRename_OneFinalValidChangeSetAndUndoRestores`; `R9A_ReflectedCompositionWithFinalUnreachableNodeIsRefusedWithoutWrites`. |
| W-AI-06, Odd part | Offline retained-candidate replay harness uses production candidate review/apply, `WorkflowPlayChecks.Effect`, journal history and a distinct Editor process for reopen. It never repairs candidate bytes or erases an existing journal. | `RetainedReplay.Run`, modes `odd` and `odd-reopen`; exact commands and receipts under the owned row directories. |

## Evidence recorded

The retained NPC review reports `StageFailed` resolving `npcs[6]` at FerrymanElian.asset. The retained Odd review reports GP-DLG-003 for 3→8 and 8→4 before composition was included. Those files remain unchanged.

Core regression XML: `artifacts/studio/verification/W-AI-03/r9-a/core-regressions/results-fixed.xml`: **19 passed, 0 failed, 0 skipped, 0 inconclusive**, including all four new projection tests. The first compiler attempt's errors and log are retained; the corrected run is the passing evidence.
Final-state edge regression XML `core-regressions/results-edges.xml`: **21 passed, 0 failed/skipped/inconclusive**. Added `R9A_DeferredCreatedTargetStillRefusesOutOfRangeFieldsBeforeWrites` and `R9A_FinalValidatorTraversesRepairedFactThroughUnchangedCondition`. `.NET` core model suite: **113 passed, 0 failed/skipped**.

Owned views/refusal XML `W-VIEW-02/r9-a-regressions-20261007T101610.953797Z/regressions.xml`: **4 passed**, including both retained R6-B refusal checks, R6-F unenrolled-graph refusal and the combined final-valid views workflow.

The initial offline NPC harness omitted catalog context and correctly refused `StaleContext`. A second attempt supplied the retained request revision and correctly refused its difference from the current catalog (`8d5bcc…` versus `00ebe875…`). Both refusal receipts remain retained; neither is a projection PASS or original-request freshness proof.

Static checks: `python3 tools/check_package_metadata.py` passed (42 packages, 92 package assemblies); `python3 tools/check_game_core_csharp.py` passed (1276 files).

### Retained candidate acceptance

- **W-AI-02 PASS:** `W-AI-02/r9-a-npc-20261007T103755.217153Z/` contains passing Stage, Apply, `ferryman2/play-effect.json` and Undo. The effect observes committed patrol motion, NavMesh placement, enrolled graph, successful dialogue start and completed conversation. Undo restores complete saved baseline bytes and removes created assets. Candidate SHA-256 remains `be610a822ede2876e6b7791fe386ae194293005b1e073b12d04c5a93a872ef3e`.
- **W-AI-03 PASS:** `W-AI-03/r9-a-odd-20261007T104156.540366Z/` contains passing Stage/Apply, unlit versus lit real-Play dialogue, and complete-byte Undo. The new line appears only on the lit path. Candidate SHA-256 remains `231344be98b3e128246655ec3362ecc4de5b3ac4a6e59cd6fdbe2fb4ddbcc4e3`.
- **W-AI-06 Odd portion PASS:** the same Odd directory records original PID 2325199 and reopened PID 2328926, an Applied journal after reopening, then successful Undo → Redo → final Undo with exact complete-byte comparisons. The row is not promoted to full PASS because this packet did not rerun the unrelated preceding HUD/quest workflows.

Both successful candidate runs have `reviewKind: OriginalRequestContext`, exact retained/current catalog equality (`8d5bcc…`) and empty full catalog diffs. The earlier mismatch was missing registration of the five production runtime tools in the harness; calling the existing `WorldLiveOpTranslator.Register` restores the original prerequisite. No successful run uses the explicit current-catalog re-review mode.

The replay exposed a second detached-state issue: content-set validators obtain their source path from AssetDatabase, which is empty for a detached copy. The trusted gameplay adapter now invokes the unchanged `NarrativeBake.Plan` with the preserved source path, retaining all mandatory content checks. Four built-in delegates share that pipeline; custom validators still run. The failing `Invalid path` receipt remains under `W-AI-02/r9-a-npc-20261007T103304.177439Z/`.

Product commits: `21c9f410` coherent projection and six regressions; `6a67e287` source-provenance content-set validation and offline replay harness. Successful workflow directories include `source-sha256.json` binding exact product/harness sources; the first successful NPC launcher began before the latter commit but ran those same source bytes. Package dependency boundary tests also pass (2/2).

Final combined EditMode XML `W-VIEW-02/r9-a-final-regressions/results.xml`: **161 passed / 1 failed / 0 skipped / 0 inconclusive**. The sole failure is the exact out-of-scope P2_3 named test, now refused at its empty graph creation with GP-DLG-001. All selected core, package-views, R9_A, R6_B and R6_F cases pass. This failure is not hidden by narrowing the reported suite.

Pre-fix control: temporarily substituted only `ChangeSetEngine.cs` from baseline `64525157`, ran the six new core tests, then restored the exact committed engine bytes. XML `W-AI-03/r9-a/baseline-control/results.xml` records **2 passed / 4 failed**: deferred numeric checks, transitive repaired reference, compose-then-connect and final orphan refusal all fail without the fix. The two fixture-created-reference tests pass at baseline because their fixture types do not have the real roster validator; the unchanged P4.2i NPC StageFailed witness and passing real NPC replay establish that finding's before/after evidence.

Owned row registry update: **59 PASS / 6 BLOCKED / 3 FAIL**, 68 rows. Only W-AI-02, W-AI-03, W-AI-06 and W-VIEW-02 entries changed; the other 64 entries retain their existing values. This is a mixed-revision packet update, not a new all-row same-revision qualification.

After restoring the final engine, `W-AI-03/r9-a/final-control/results.xml` records **7/7 PASS**, including all six core regressions and the combined views workflow. This final control follows the intentionally failing baseline experiment; working source is restored byte-for-byte.

Cleanup: restored Unity-generated GraphicsSettings/QualitySettings changes and the importer-deleted tracked Cargo.lock.meta; removed only newly generated unrelated folder metadata and URPProjectSettings. The two acceptance journals remain locally in `games/hollowmere/Studio/History/2026/10/`, both Undone, and are not committed outside the exclusive paths. Creator history and pre-existing `.omp/` are untouched. No installed service was modified.

## Requests to other packets

`games/hollowmere/Assets/Hollowmere/Tests/P2_3/EditMode/HollowmereEditingViewsTests.cs`, `Dialogue_AddLineConnectRename_AreJournaledChangeSetsAndUndoRestores`: this exact named test is outside R9-A's exclusive `Tests/(R9_A|P3_2)` paths. It creates an empty graph and commits individual intermediate edits; the second AddLine is unreachable until a later, separately committed Connect. Change the fixture/workflow to create a valid baseline and submit AddLine+Connect+Rename as one change set using the existing `ViewEdits`/`DialogueEdits` builders, then retain its journal and full undo assertions. The owned R9_A regression demonstrates that contract. Do not weaken GP-DLG-005 or treat a separately committed orphan as valid.

## Left open

The exact P2_3 named-test acceptance criterion cannot be claimed from the owned replacement test. Its owner path needs the change above. W-VIEW-02 must not be promoted based solely on the replacement.

No installed companion or etosd restart, key-file read, paid provider operation, or worker-output modification is part of this packet. Retained candidate replay is not a newly generated worker request. W-AI-06 evidence from this packet covers the Odd-edit portion, not a fresh replay of unrelated HUD/quest edits.
