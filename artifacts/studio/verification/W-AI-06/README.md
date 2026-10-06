# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **FAIL**. Fresh R6-B narrative session saves three Applied entries; a separate Editor reopens with all saved hashes equal, and all undo/redo/final undo calls succeed. Exact final byte consistency fails: backToBefore=false, and the driver now exits 1. Odd and DrownedBell differ only in contentStamp; HUD and Lantern are byte-identical. No bake or stamp fields are normalized. Before/after bytes and diffs are retained.

Report timestamp: 2026-10-06T15:29:44.406256+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py narrative; python3 artifacts/studio/verification/TOOLS/live-p42e.py reopen
```

## Retained evidence

- [W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/narrative/saved.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/narrative/saved.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/result.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/result.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/final.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/final.json)
- [../workflows/P4.2e/reopen-diff-analysis.json](../../workflows/P4.2e/reopen-diff-analysis.json)

Exact acceptance/component cases: `R6_Request5_RetainedReopenMismatchFailsExactByteContract`, `R6_Request5_RealReopenFinalStepFailsRatherThanOnlyRecordingFalse`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
