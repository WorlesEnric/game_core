# W-AI-05: Change the lantern quest to require two oil flasks; verify consequences in Play

Verdict: **PASS**. The live two-operation candidate applies against the real indexed OilFlask identity. The simulator no longer refuses GP-QST-004, and the actual Play observer confirms stage 1 after one flask and stage 2 after two; the driver records pass.

Report timestamp: 2026-10-06T12:40:21.173851+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d narrative
```

## Retained evidence

- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/play-effect.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/play-effect.json)
- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/candidate.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/candidate.json)
- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/apply-report.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/quest/apply-report.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
