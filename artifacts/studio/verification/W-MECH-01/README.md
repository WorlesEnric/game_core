# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **PASS**. Signed Docker stages pass in 161.873s cold and 80.541s warm; creator admissions restore Play in 61.052s and 24.988s, preserve nine coins, pass 120-step Pending-to-Passed smoke and verified Undo. Initial bake_stale preflight refusal is retained; ordinary production rebake preceded new explicit attempts.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42l.py mechanism --attempt rebaked
```

## Current-run evidence

- [W-MECH-01/p42l-acceptance/result.json](../W-MECH-01/p42l-acceptance/result.json)
- [W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/restored-world.json](../W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/restored-world.json)
- [W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/smoke-witness.json](../W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/smoke-witness.json)
- [W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/undo.json](../W-MECH-01/p42l-stage-review-cold-20261008T001200.830681Z/undo.json)
- [W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/restored-world.json](../W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/restored-world.json)
- [W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/smoke-witness.json](../W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/smoke-witness.json)
- [W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/undo.json](../W-MECH-01/p42l-stage-review-warm-20261008T001636.910123Z/undo.json)
- [W-MECH-01/p42l-signed-record-20261008T001156.744408Z/verified.json](../W-MECH-01/p42l-signed-record-20261008T001156.744408Z/verified.json)
- [W-MECH-01/p42l-signed-record-20261008T001633.738482Z/verified.json](../W-MECH-01/p42l-signed-record-20261008T001633.738482Z/verified.json)
