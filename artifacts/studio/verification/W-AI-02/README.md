# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **FAIL**. R6-B rejects the unchanged retained unreachable-entry witness before writes (GP-DLG-005, nodes 0–7). The fresh installed-worker ferryman candidate instead preserves reachability and applies two operations, creating the 21st entity, but actual Play fails: applied NPC needs patrol and dialogue. After Play reload the candidate UI is Invalid, so the unchanged driver skips undo and records a 21-entity final roster. A separate fresh-Editor normal History.Undo passes (1/1 XML), restores 21→20 and journals Undone; no candidate bytes were repaired.

Report timestamp: 2026-10-06T15:29:44.398965+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py text2
```

## Retained evidence

- [W-AI-02/p42e-text2-20261006T150123.555155Z/result.json](../W-AI-02/p42e-text2-20261006T150123.555155Z/result.json)
- [W-AI-02/p42e-text2-20261006T150123.555155Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42e-text2-20261006T150123.555155Z/workflow/ferryman2/play-effect.json)
- [W-AI-02/p42e-text2-20261006T150123.555155Z/workflow/ferryman2/candidate.json](../W-AI-02/p42e-text2-20261006T150123.555155Z/workflow/ferryman2/candidate.json)
- [R6-P4.2e/p42e-regression-20261006T143939.136591Z/result.json](../R6-P4.2e/p42e-regression-20261006T143939.136591Z/result.json)
- [W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z/result.json](../W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z/result.json)
- [W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z/normal-undo.json](../W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z/normal-undo.json)

Exact acceptance/component cases: `R6_Request6_ActualEngineStageRejectsUnmodifiedFerrymanBeforeAnyWrite`, `WorkflowPlayChecks.Effect("W-AI-02", "ferryman2")`, `R2_38_P42e_NormalJournalUndoAfterNpcPlayReload`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
