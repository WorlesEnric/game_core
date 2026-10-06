# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. P4.2f current-main installed stages pass in 156.122 s cold / 80.801 s warm, each with 36 EditMode + 2 PlayMode XML passes and authenticated signed world/predicted hashes. Both graphical Play admissions and one retry each roll back with compile_timeout. Owned UPM admission resolves take 17.077 / 19.286 / 7.873 / 20.098 s; logs then repeatedly report Awaiting authenticated companion verdict refresh after domain reload. The historical 146 s native resolve is not reproduced. No live restoration, tri-state smoke or successful admission undo is claimed. Negative candidate has no passing verdict and Admit stays disabled; production Docker Roslyn separately refuses 21 findings across SG001–SG010.

Product revision: `4ac7ba858b91e73e2d5de9dc6f02852c13feec56`. Earlier attempts remain historical evidence.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42f.py stage-submit --candidate artifacts/studio/workflows/P4.2f/candidate
# Follow README-P4.2f for service wait, signed receipt and graphical review.
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
