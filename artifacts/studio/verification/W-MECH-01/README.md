# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. Installed R4-A/R3-C app-origin stage verifies through the authenticated service and enables Admit; the visible badge still says not staged. Docker cold 142.260 s / warm 62.905 s; each XML has 33 EditMode + 2 PlayMode passes. Actual Play Admit refuses catalog_missing because Entry.Verify opens Editor scenes during Play. No capture/restore/smoke/undo follows. Negative fixture is refused with Admit disabled (14 lexical hits).

Report timestamp: 2026-10-06T09:08:50.185073+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
See artifacts/studio/verification/TOOLS/README-P4.2c.md: stage-submit, watch-stage-p42c.py, stage-review.
```

## Retained evidence

- [W-MECH-01/p42c-signed-receipts-20261006T090137.253975Z/result.json](../W-MECH-01/p42c-signed-receipts-20261006T090137.253975Z/result.json)
- [W-MECH-01/p42c-stage-submit-20261006T075911.180322Z/service/job.json](../W-MECH-01/p42c-stage-submit-20261006T075911.180322Z/service/job.json)
- [W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/service/job.json](../W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/service/job.json)
- [W-MECH-01/p42c-stage-review-20261006T081247.078280Z/play-verification.json](../W-MECH-01/p42c-stage-review-20261006T081247.078280Z/play-verification.json)
- [W-MECH-01/p42c-stage-review-20261006T081247.078280Z/admit.json](../W-MECH-01/p42c-stage-review-20261006T081247.078280Z/admit.json)
- [W-MECH-01/p42c-play-catalog-diagnostic-20261006T081636.630932Z/catalog-preflight.json](../W-MECH-01/p42c-play-catalog-diagnostic-20261006T081636.630932Z/catalog-preflight.json)
- [W-MECH-01/p42c-stage-review-negative-20261006T082331.934190Z/panel-verdict.json](../W-MECH-01/p42c-stage-review-negative-20261006T082331.934190Z/panel-verdict.json)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
