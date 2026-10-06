# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **PASS**. P4.2g installed main after R6-E/R6-F/R6-G: cold/warm signed Docker stages pass in 158.011/79.598 s, each 36 EditMode + 2 PlayMode XML passes. Explicit graphical Play Admit restores nine coins, completes 120-frame Pending-to-Passed smoke and normal undo twice. Cold durable-UTC admission is 42.869 s; corrected warm timer 47.070 s. Owned UPM admission/undo resolves are 16.622/19.119 s cold and 15.891/19.461 s warm. Both reload traces establish authenticated service through the R6-E resumer with zero refresh waits. No admission timeout or timeout retry occurred. Negative app candidate cannot Admit; separate production Docker Roslyn refuses 21 findings across SG001-SG010. A pre-Admit wrong-filename launch and the old cold harness timezone arithmetic remain retained, not rewritten.

Product revision: `55091b74be95eb6e47fe0e33c01ce2d8f2528779`. Earlier attempts remain historical evidence.

## Reproduce

```sh
See artifacts/studio/verification/TOOLS/README-P4.2g.md: stage-submit, authenticated receipt-stage, graphical stage-review for the two retained envelopes.
```

## Retained evidence

- [W-MECH-01/p42f-stage-submit-20261006T170309.995129Z](../W-MECH-01/p42f-stage-submit-20261006T170309.995129Z)
- [W-MECH-01/p42f-stage-submit-20261006T171632.907547Z](../W-MECH-01/p42f-stage-submit-20261006T171632.907547Z)
- [W-MECH-01/p42f-stage-review-20261006T170945.223444Z](../W-MECH-01/p42f-stage-review-20261006T170945.223444Z)
- [W-MECH-01/p42f-stage-review-20261006T171323.900006Z](../W-MECH-01/p42f-stage-review-20261006T171323.900006Z)
- [W-MECH-01/p42f-stage-review-20261006T172116.443048Z](../W-MECH-01/p42f-stage-review-20261006T172116.443048Z)
- [W-MECH-01/p42f-stage-review-20261006T172434.103813Z](../W-MECH-01/p42f-stage-review-20261006T172434.103813Z)
- [W-MECH-01/p42f-stage-review-negative-20261006T172910.619626Z](../W-MECH-01/p42f-stage-review-negative-20261006T172910.619626Z)
- [W-MECH-01/p42f-receipt-stage-20261006T172113.588454Z](../W-MECH-01/p42f-receipt-stage-20261006T172113.588454Z)
- [W-MECH-01/p42g-receipt-stage-20261006T191613.095454Z](../W-MECH-01/p42g-receipt-stage-20261006T191613.095454Z)
- [W-MECH-01/p42g-receipt-stage-20261006T191613.151367Z](../W-MECH-01/p42g-receipt-stage-20261006T191613.151367Z)
- [W-MECH-01/p42g-receipt-stage-20261006T192303.876009Z](../W-MECH-01/p42g-receipt-stage-20261006T192303.876009Z)
- [W-MECH-01/p42g-semantic-20261006T191238.035251Z](../W-MECH-01/p42g-semantic-20261006T191238.035251Z)
- [W-MECH-01/p42g-stage-review-20261006T191631.705512Z](../W-MECH-01/p42g-stage-review-20261006T191631.705512Z)
- [W-MECH-01/p42g-stage-review-20261006T191739.034437Z](../W-MECH-01/p42g-stage-review-20261006T191739.034437Z)
- [W-MECH-01/p42g-stage-review-20261006T192308.082336Z](../W-MECH-01/p42g-stage-review-20261006T192308.082336Z)
- [W-MECH-01/p42g-stage-review-negative-20261006T192617.136441Z](../W-MECH-01/p42g-stage-review-negative-20261006T192617.136441Z)
- [W-MECH-01/p42g-stage-submit-20261006T191228.517592Z](../W-MECH-01/p42g-stage-submit-20261006T191228.517592Z)
- [W-MECH-01/p42g-stage-submit-20261006T192039.511265Z](../W-MECH-01/p42g-stage-submit-20261006T192039.511265Z)
- [W-MECH-01/p42g-stage-submit-negative-20261006T192510.393381Z](../W-MECH-01/p42g-stage-submit-negative-20261006T192510.393381Z)
