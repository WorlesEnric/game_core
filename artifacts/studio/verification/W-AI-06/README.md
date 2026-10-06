# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **PASS**. Unchanged retained narrative candidates save in Editor 4005226 and reopen in separate Editor 4007734 with all saved hashes equal. Normal undo/redo/final undo plus the production deterministic bake restores all four complete asset files exactly, including contentStamp: backToBefore=true. No fields or candidate stamps are normalized; no new paid generation.

Source: `1a462f88`. [R7-A proof and exact limitations](r7-a/README.md).

## Reproduce

```sh
bash artifacts/studio/verification/W-AI-06/r7-a/run.sh /absolute/fresh/evidence-directory
```

## Retained evidence

- [W-AI-06/r7-a/README.md](../W-AI-06/r7-a/README.md)
- [W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/narrative/saved.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/narrative/saved.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/result.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/result.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/final.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/final.json)
- [../workflows/P4.2e/reopen-diff-analysis.json](../../workflows/P4.2e/reopen-diff-analysis.json)
