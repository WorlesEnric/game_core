# W-AI-04: Edit the HUD objective label and rebind it to the quest stage name

Verdict: **PASS**. The fresh installed-worker HUD candidate applies and saves; a separate Editor reopens with matching saved hashes, and normal undo/redo/final undo succeeds. HUD bytes return exactly to baseline.

Report timestamp: 2026-10-06T15:29:44.403575+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py narrative; python3 artifacts/studio/verification/TOOLS/live-p42e.py reopen
```

## Retained evidence

- [W-AI-03/p42e-narrative-20261006T151348.703137Z/result.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/result.json)
- [W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/hud/candidate.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/hud/candidate.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/after-reopen.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/after-reopen.json)

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
