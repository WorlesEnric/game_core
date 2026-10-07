# R9-C — dialogue creator commands under final-state validation

This is the packet's PACKET.md. Branch `omp/r9-c`, based on `96c5df9`; Linux build host, 2026-10-07.

## Creator-facing contract

- **New graph** creates an asset with entry index 0 and one Line node in a single journaled `create` change set, then selects that entry. There is no committed empty-graph intermediate state.
- **Add line / Add choice** requires a selected non-End node with a free continuation port. No selection or an occupied continuation leaves the graph unchanged and reports the selection requirement. This deliberately does not silently replace an occupied edge and orphan its old continuation.
- Terminal **Next** uses the existing gameplay tool's `after` argument: the tool adds the node and its incoming edge atomically, exactly as in the R5-B/P4.2d guide flow.
- **Choice Option / Branch Else** uses add plus a dependent edge-list `set` in one AllOrNothing change set. Adding a choice also preserves the new choice's terminal option edges. The final projected state, never the intermediate orphan, is validated.
- **Connect / Rename** remain single-op change sets. Invalid connections, removals, disconnections or field values are still refused by the engine; the view does not relax GP-DLG-001/003/005 or claim that arbitrary user input is valid.
- Low-level `DialogueEdits.AddLine` remains a composable operation builder, including `after=-1`, for callers that construct a complete final-valid multi-op change set. R9-A's workflow relies on that seam and is unchanged.

## R2 fixes

| Finding / row | Fix | Regression |
|---|---|---|
| R2-38 / W-VIEW-02, GP-DLG-001 | Creator New graph initializes an entry, rather than committing an empty graph. | `Dialogue_AddLineConnectRename_AreJournaledChangeSetsAndUndoRestores` |
| R2-38 / W-VIEW-02, GP-DLG-005 | Selected-port additions are final-valid in one change set; no-selection and occupied-port additions make no writes. Connect and both line/option renames remain single-op. | Same exact P2_3 named test: creator sequence, Applied/Manual journal records, full-byte undo at every step, then undo creation removes the asset. |
| R9-C choice-port composition | Add choice from a choice preserves the new node's terminal options as well as its incoming edge. | `R9C_AddChoiceFromChoice_PreservesBothOptionsAndUndoBytes` |

The existing pre-fix witness is `artifacts/studio/verification/W-VIEW-02/r9-a-final-regressions/results.xml`: the exact P2_3 named test fails with GP-DLG-001. The preceding P4.2i orphan failure remains historical evidence; no validator weakening or substitute test promotion is used.

## Verification

Requested EditMode suite ran through `studio/tools/unity-batch.sh`, holding one host slot, with filter `GameCore\.Studio\.Views.*|Hollowmere\.(P2_3|R9_[AC]|R6_B).*`. Final XML at `artifacts/studio/verification/W-VIEW-02/r9-c/results-final.xml`: **63 passed / 0 failed / 0 skipped / 0 inconclusive**, product `a89f55bbbdd90986fdaa8a9040aba2e1601fd65f`. The exact P2_3 named test, unchanged R9-A workflow, new choice-port test and every selected R6-B case pass. Source digests and the exact command are retained under the row evidence directory/README.

First full run `r9-c/results.xml`: **62 passed / 1 failed**, the unrelated 2,000-node layout frame at 18.7479 ms against 16 ms; all dialogue cases passed. The final full suite passes with the layout test unchanged. Both results are retained; no threshold relaxation or narrower final filter was used.

Pre-fix control `r9-c/baseline-port.xml`: **0 passed / 1 failed**, with only AddChoice temporarily restored to its old handler. The new behavior assertion fails because Option 0 has no target (expected node 1). The fixed implementation was restored byte-for-byte before final verification. Earlier `baseline-control.xml` failed first on operation count; it is retained but not substituted for the behavior-first control.

Static check receipts: `r9-c/static-checks.json`. `python3 tools/check_package_metadata.py`: 42 packages, 92 assemblies, PASS. `python3 tools/check_game_core_csharp.py`: 1,278 files, PASS. No Rust or dotnet source changed; no such suite is claimed.

W-VIEW-02 is now PASS; registry totals are **60 PASS / 6 BLOCKED / 2 FAIL**, 68 rows. Only this row's disposition and the requested aggregate totals were promoted; other row records remain byte-equivalent JSON values. This is mixed-revision qualification, not a new same-revision all-row pass.

Cleanup after verification: restored test-generated HollowmereContent.asset and importer-deleted Cargo.lock.meta to their original committed bytes; removed only newly generated test-folder metadata. Pre-existing `.omp/` is untouched. No throwaway script or temporary control implementation remains.

## Requests to other packets

None. No core/gameplay seam change is needed. Shared documentation edits are limited to the explicitly requested W-VIEW-02 registry/matrix cell and totals, plus the owned R9-C section of P2.3.

## Left open

Batch-mode execution exercises the real DialogueView commands and engine, not interactive OS file-picker clicks. This packet does not claim a new graphical/real-Play capture; those portions retain explicitly attributed P4.2i evidence. No companion/etosd restart, credentials read, paid operation, or sibling-clone mutation is involved.
