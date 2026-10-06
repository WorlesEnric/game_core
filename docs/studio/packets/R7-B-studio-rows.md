# R7-B Studio rows — packet report

Branch: `omp/r7-b`. Base: `f4b9d00`. Product/driver commits: `0539abd9`, `cb66df4c`, `7061c5cd`, `0909e4a2`, `de18d3b9`. Only the assigned source paths and six evidence rows changed, plus the explicitly authorized ROWS/SUMMARY/matrix cells. No paid operations, installed companion/etosd changes, sibling-clone changes or display `:1` use.

## Results

| Row | Disposition | Exact proof / remaining seam |
|---|---|---|
| [W-UI-02](../../../artifacts/studio/verification/W-UI-02/README.md) | BLOCKED | Real three-NPC/fence marquee and occluded point choices pass below the UI; missing creator marquee-result chooser belongs to R7-A. |
| [W-UI-03](../../../artifacts/studio/verification/W-UI-03/README.md) | BLOCKED | Real lantern Body/logical selection passes; absent prefab/scope chooser belongs to R7-A. |
| [W-MODEL-02](../../../artifacts/studio/verification/W-MODEL-02/README.md) | PASS | Actual delete preview identifies dialogue choice line and collect objective; engine deletion and History undo succeed. |
| [W-EDIT-04](../../../artifacts/studio/verification/W-EDIT-04/README.md) | PASS | One combined real NPC edit → exit Play → apply → delete → apply sequence yields journaled StaleTarget; normal inverse restores original content. |
| [W-EDIT-06](../../../artifacts/studio/verification/W-EDIT-06/README.md) | PASS | Committed runtime-only world move → Changes creator command → durable separate authored change → Play exit → save/reopen → ordinary inverse/reopen. |
| [W-PERSIST-02](../../../artifacts/studio/verification/W-PERSIST-02/README.md) | PASS | Literal referenced prefab rename, different Editor PID, real SaveService restoration and exact NPC/cosmetic state; original prefab/meta restored byte-identically. |

Matrix after these four promotions: **39 PASS / 26 BLOCKED / 3 FAIL**, 68 rows. Other rows retain their previous revision-specific dispositions.

## R2 fixes

| Finding / row | Fix or exact driver | Test / evidence |
|---|---|---|
| R2-06 / W-EDIT-06 | `runtime.move` is RuntimeOnly/Live and calls production `GameplayCommands.Place` through the existing reflection bridge. It never mutates the authored proxy or supplies a runtime inverse. `Apply to authored` persists a distinct Candidate against the saved pre-Play stamp, then uses ordinary move/History after exit. | `RuntimeMoveAcceptance.W_EDIT_06_RealRuntimeMoveCreatorPromotionExitPlayAndInverse`; six `RuntimeMovePromotionTests` cases; final XML 8/8 including item impact. |
| R2-05 / W-EDIT-06 | The new runtime branch preserves `Conflict.data.expected/actual` world-revision witnesses, not just the code. | `W_EDIT_06_StaleRuntimeWorldRefusesWithRevisionWitnesses` fails before the correction in `conflict-before/results.xml`, passes in final XML. |
| R2-36 / R2-38 / W-MODEL-02 | Real item, dialogue availability condition and quest assets; exact nested paths in core impact and actual delete preview, then deletion and undo. No index implementation fix was necessary for this case. | `W_MODEL_02_DeleteItemListsReferencingDialogueLineAndObjective`; `nodes[0].options[0].condition` at depth 2 and `objectives[0].item` at depth 1. |
| R2-38 / W-EDIT-04 | Reload-resilient combined acceptance using shipped Bram, production NPC command, exact serialized staged edit, engine deletion and normal History. | `ScenariosR7Lifecycle.EditLifecycle`, `edit-result.json: pass`; final outcome `Refused/StaleTarget`, no resurrection. |
| R2-38 / W-PERSIST-02 | Durable preimage/handoff before literal prefab rename; separate Editor reopens, restores save into a new world, then restores original prefab path/bytes/meta. | `PersistPrepare` returns prepared only; `PersistReopen` returns pass with PIDs 4020708/4024953 and matching canonical slot hash/NPC fields. |
| R2-38 / W-UI-02/03 | Exact isolated saved scene fixtures, real picking/controller commands and identity assertions. Missing UI remains explicit rather than simulated. | `ScenariosR7Picking.RunFence` and `RunLantern`: BLOCKED receipts, `error: null`; no synthetic capture. |

## Engine and view contracts

- `ChangeSetEngine.RuntimeMoves : RuntimeMovePromotion`.
- `IRuntimeMoveGateway.Move(AuthoringRef, Vector3, float)` and `IsAt(AuthoringRef, Vector3, float, string operationId)` bind the real placement command and committed pose/request receipt.
- `RuntimeMovePromotion.CaptureAuthoredState()` captures unique saved entities in open scenes before Play, never runtime transforms.
- `TryApplyToAuthored(string runtimeChangeSetId, out ChangeSet? authored, out Diagnostic? problem)` requires the same live world, committed request and current canonical pose. It persists a separate Candidate before publishing it to History; duplicate requests reuse its ID.
- `ApplyPending()` applies and saves after Play/domain reload. Changed/deleted targets retain ordinary Conflict/StaleTarget. Runtime actions remain non-undoable and cannot join atomic authored batches.
- `ViewEdits.ApplyToAuthored(...)` and `ChangesView.ApplyToAuthored(string)` expose the creator command in Changes / Journal. Views binds the production gateway when its context opens.

Dirty/unsaved scenes, missing/ambiguous pre-Play identity, other runtime actions, uncommitted and wrong-world moves remain non-promotable. This intentionally does not guess an authored target from runtime-only/spawned content.

## Requests to other packets

**R7-A / W-UI-02:** `Packages/com.gamecore.studio.ui/Editor/Viewport/StudioViewportWindow.cs`, `MarqueeSelect(Rect, SelectionOp, bool?)` currently selects the marquee result without opening an overlap chooser. Present resulting candidates and allow choosing the three NPC identities while excluding fence geometry. A later point-click list is not the required marquee sequence. Expose stable button names for the real acceptance driver.

**R7-A / W-UI-03:** `Packages/com.gamecore.studio.ui/Editor/Viewport/ViewportSupport.cs`, `OverlapPopup.Show(IReadOnlyList<PickCandidate>, Vector2, Func<AuthoringRef,string>)`, and `ViewportPicker.cs`, `Choose(PickCandidate, bool, SelectionOp)`: add explicit prefab and authoring-scope choices alongside logical/subpart. Prefab resolves the originating Lantern prefab; Instance scope preserves logical lantern identity with `AuthorScope.Instance`; subpart retains `Mesh:Body`; logical clears it. Expose stable button names. No UI-owned source was changed here.

## Verification

All Unity invocations used `studio/tools/unity-batch.sh`, one Editor at a time for this packet, under the shared host allocator. ETOS autostart/live gates were zero. Exact commands and retained attempts are linked from the six row READMEs.

- Broad core/views/R7_B EditMode XML: **137 passed / 2 failed / 0 skipped**. Core 95/96; Views 26/26; Hollowmere Views 15/15; item acceptance 1/1; initial promotion driver 0/1. CORE-PICK required an idle host and observed three Editors: the owner anticipated this concurrent-host precondition failure; it was not weakened or fixed.
- Initial real promotion driver failures are retained: un-authored decoration selected by name, then iterator state/closure lost across actual Play reload. The driver now uses the shipped stable authored ID, separates reload phases and retains witnesses in SessionState. The standalone corrected real-Play acceptance passes 1/1.
- Final source `de18d3b9`: **8/8 EditMode passed**, zero skipped, comprising six promotion boundaries, real Play promotion/inverse and exact item deletion impact. Read directly from [XML](../../../artifacts/studio/verification/W-EDIT-06/r7-b-final/results.xml).
- Missing revision witness regression: **0/1 before**, **1/1 in final**; both XMLs retained.
- .NET Model: **113/113 passed**, zero skipped, [TRX](../../../artifacts/studio/verification/W-EDIT-06/r7-b-dotnet/r7b-model.trx).
- Lifecycle executeMethods: W-EDIT-04 pass; persistence prepared then separate-process reopen pass. Their asserted JSON state transitions establish acceptance; no NUnit XML is claimed for executeMethods.
- Picking executeMethods: both expected BLOCKED exits 2 with no assertion errors. The wrapper conservatively reports failure for nonzero exit; row receipts distinguish blocked from failed.
- Final metadata checker passes: 42 packages / 91 package assemblies. Final C# checker passes: 1,240 files.

## Cleanup and evidence integrity

Driver-owned fixture scenes/assets are removed. The lifecycle driver performs normal History inverse and byte-checks original scene/prefab content. Broad-suite generated content-list drift, Unity-created unrelated folder metadata/URP settings and this run's three project History files were removed/restored to their initial bytes; their required History snapshots are retained under the owned row. No user changes were discarded; the initial tree was clean except `.omp/`, which remains untouched.

Own XML/log/JSON evidence has only absolute repository/home paths scrubbed; outcomes and assertions are unchanged. Save checkpoint and prefab preimage bytes remain untouched so their recorded hashes remain verifiable. Evidence files are under 20 MiB each. SHA256 manifests accompany retained runs.

## Left open

Only the two exact R7-A creator UI seams above block the assigned UI rows. Below-UI component success is not a full-row pass.

Capture limitation: `unity-batch.sh` hardcodes `-nographics`; existing `UnityWindowCapture` uses GUIView grabs and cannot capture with a Null graphics device. Both drivers record this and have a real offscreen capture path for a graphics-enabled batch Editor; no synthetic PNG or display `:1` was used. Captures must accompany qualification after the missing UI commands land.

CORE-PICK's idle-host precondition remains unqualified under concurrent packets. This does not relabel its existing matrix row or claim a new timing pass.
