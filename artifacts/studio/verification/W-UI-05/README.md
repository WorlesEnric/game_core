# W-UI-05: Selection/hover and marquee timings

Verdict: **PASS**. CORE-PICK on main 40fb91fa passes both datasets: each has 100 picks and 100 marquees over 500 distinct candidates. Pick p95 1.2553/0.8460 ms; marquee p95 0.3410/0.1970 ms, against unchanged 16/50 ms budgets. The separate 21-update timing regression also passes; combined XML 3/3.

Report timestamp: 2026-10-06T15:29:44.365842+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d selection
studio/tools/verify-all.sh p42d selection-repeat
```

## Retained evidence

- [W-UI-05/p42d-selection-20261006T112100.063994Z/results.xml](../W-UI-05/p42d-selection-20261006T112100.063994Z/results.xml)
- [W-UI-05/p42d-selection-20261006T112100.063994Z/selection.json](../W-UI-05/p42d-selection-20261006T112100.063994Z/selection.json)
- [W-UI-05/p42d-selection-repeat-20261006T123743.261129Z/results.xml](../W-UI-05/p42d-selection-repeat-20261006T123743.261129Z/results.xml)
- [W-UI-05/p42d-selection-repeat-20261006T123743.261129Z/selection.json](../W-UI-05/p42d-selection-repeat-20261006T123743.261129Z/selection.json)

Exact acceptance/component cases: `R2_38_B_SELECT_100PicksAnd500CandidateMarquee`, `R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms`. Component cases do not close any missing external workflow.

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
