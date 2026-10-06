# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. R6-A installed stages pass in 157.145 s cold and 75.479 s warm, each with 36 EditMode + 2 PlayMode XML passes and authenticated signed world/predicted catalog hashes. Both graphical Play Admit attempts terminate in production rollback with compile_timeout after Unity Package Manager stalls at Installing packages (76/76). The second attempt retains the same compile_timeout; package and pending record are removed by rollback. No live restore, tri-state smoke or successful admission undo is inferred from isolated stage smoke. Negative candidate: 14 lexical hits, no issued passing verdict, Admit disabled. A separate real Docker Roslyn scan refuses the exact negative-semantic sources with 21 findings across SG001–SG010.

Report timestamp: 2026-10-06T15:29:44.424602+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
See TOOLS/README-P4.2e.md: stage-submit, watcher, receipt-stage, stage-review.
```

## Retained evidence

- [W-MECH-01/p42e-stage-submit-20261006T144109.661452Z/service/job.json](../W-MECH-01/p42e-stage-submit-20261006T144109.661452Z/service/job.json)
- [W-MECH-01/p42e-stage-submit-20261006T145038.813597Z/service/job.json](../W-MECH-01/p42e-stage-submit-20261006T145038.813597Z/service/job.json)
- [W-MECH-01/p42e-receipt-stage-20261006T145345.507609Z/result.json](../W-MECH-01/p42e-receipt-stage-20261006T145345.507609Z/result.json)
- [W-MECH-01/p42e-stage-review-20261006T144510.582506Z/result.json](../W-MECH-01/p42e-stage-review-20261006T144510.582506Z/result.json)
- [W-MECH-01/p42e-stage-review-20261006T145348.148583Z/result.json](../W-MECH-01/p42e-stage-review-20261006T145348.148583Z/result.json)
- [W-MECH-01/p42e-stage-review-negative-20261006T150052.077560Z/outcome.json](../W-MECH-01/p42e-stage-review-negative-20261006T150052.077560Z/outcome.json)
- [W-MECH-01/p42e-stage-submit-negative-20261006T145836.138161Z/service/job.json](../W-MECH-01/p42e-stage-submit-negative-20261006T145836.138161Z/service/job.json)

Exact acceptance/component cases: `R2_09_13_P42e_InstalledSignedRecordHasWorldAndPredicted`, `P42e.Live.StageUi.Review`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
