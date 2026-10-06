# W-AI-05: Change the lantern quest to require two oil flasks; verify consequences in Play

Verdict: **PASS**. The fresh installed-worker candidate uses the indexed OilFlask identity. Actual Play holds quest stage 1 after one flask and advances to stage 2 after two; the driver records pass.

Report timestamp: 2026-10-06T15:29:44.404795+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py narrative; python3 artifacts/studio/verification/TOOLS/live-p42e.py reopen
```

## Retained evidence

- [W-AI-03/p42e-narrative-20261006T151348.703137Z/result.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/result.json)
- [W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/quest/candidate.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/quest/candidate.json)
- [W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/after-reopen.json](../W-AI-06/p42e-reopen-20261006T152055.608370Z/workflow/reopen/after-reopen.json)
- [W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/quest/play-effect.json](../W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow/quest/play-effect.json)

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
