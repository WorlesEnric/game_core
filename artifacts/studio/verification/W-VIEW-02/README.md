# W-VIEW-02: Dialogue: Graph tools, conditions, preview, journaled edits/undo and real-Play bridge

Verdict: **PASS**, R9-C at product `a89f55bbbdd90986fdaa8a9040aba2e1601fd65f`. The exact named P2_3 creator workflow now creates an entry graph, adds connected nodes atomically, renames and connects with single-op edits, and restores complete bytes through each journal undo. Final requested EditMode suites: **63 passed / 0 failed / 0 skipped / 0 inconclusive**. [Final XML](r9-c/results-final.xml), [source digests](r9-c/source-sha256.json), [packet](../../../../docs/studio/packets/R9-C-views-dialogue-edit.md).

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
P4.2i's graphical/real-Play evidence below remains explicitly attributed historical evidence; R9-C freshly closes the failed editing/undo criterion rather than claiming a new all-row same-revision qualification.

## Reproduce

```sh
studio/tools/unity-batch.sh --project /home/worlesenric/wkspace/gc-studio/r9-c/games/hollowmere --log-dir /home/worlesenric/wkspace/gc-studio/r9-c/artifacts/studio/verification/W-VIEW-02/r9-c --label views-dialogue-final --results /home/worlesenric/wkspace/gc-studio/r9-c/artifacts/studio/verification/W-VIEW-02/r9-c/results-final.xml -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\.Views.*|Hollowmere\.(P2_3|R9_[AC]|R6_B).*'
```

## Historical P4.2i evidence

- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/result.json)
- [UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml](../UNITY-HOLLOWMERE/p42b-final-views-20261007T045056.957279Z/results.xml)
- [W-VIEW-01/captures-20261007T045200Z/capture.json](../W-VIEW-01/captures-20261007T045200Z/capture.json)

## Historical R9-A final-state workflow

Owned combined AddLine+Connect+Rename workflow and three retained true-refusal cases pass: [4/4 XML](r9-a-regressions-20261007T101610.953797Z/regressions.xml). The exact named P2_3 fixture remains outside R9-A's exclusive paths and commits invalid intermediate change sets. This row remains FAIL; replacement-test evidence does not silently satisfy the named acceptance criterion. Exact owner request: [R9-A packet](../../../../docs/studio/packets/R9-A.md#requests-to-other-packets).

## R9-C verification and controls

- Exact required `Dialogue_AddLineConnectRename_AreJournaledChangeSetsAndUndoRestores`, unchanged `Hollowmere.R9_A.FinalDialogueWorkflowTests` and all selected R6-B checks pass in [final XML](r9-c/results-final.xml).
- New `R9C_AddChoiceFromChoice_PreservesBothOptionsAndUndoBytes` fails against the old AddChoice handler: the selected Option has no target, despite the old tool adding a Next edge. [Behavior-first baseline XML](r9-c/baseline-port.xml), 0 passed / 1 failed. Only the AddChoice handler was temporarily reverted; the final implementation was restored before the passing full run. An earlier control failed first at the operation-count assertion; [that XML](r9-c/baseline-control.xml) is retained, not used as the behavioral proof.
- [First full run](r9-c/results.xml): 62 passed / 1 failed, with the unrelated layout-budget check measuring 18.7479 ms versus 16 ms. The final full run passes that unchanged check. No narrower filter or relaxed budget hides the initial failure.
- [Static checks](r9-c/static-checks.json): package metadata 42 packages / 92 assemblies; C# checker 1,278 files, both exit 0.
- The batch Editor uses NullGfxDevice. Commands, final-state validation, journal and byte restoration are freshly exercised; interactive file-picker and graphical layout were not freshly qualified.
