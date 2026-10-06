# W-AI-03: "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play

Verdict: **PASS**. The newly received live candidate applies unchanged. R5-C’s actual Hollowmere Play observer confirms the added line is absent unlit and displayed when shrine_lit=1; the driver records pass.

Report timestamp: 2026-10-06T12:40:21.172274+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d narrative
```

## Retained evidence

- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/play-effect.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/play-effect.json)
- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/candidate.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/candidate.json)
- [W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/apply-report.json](../W-AI-03/p42d-narrative-20261006T114113.853565Z/workflow/odd-line/apply-report.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
