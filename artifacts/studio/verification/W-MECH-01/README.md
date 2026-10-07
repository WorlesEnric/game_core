# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **PASS**. Current signed Docker stages pass in 163.483s cold and 81.506s warm. Creator admissions restore Play in 46.292s and 44.906s, preserve nine coins, pass 120-step Pending-to-Passed smoke and verified Undo. Initial cache_invalid prerequisite refusal and stale-harness revision assertion failures are retained; neither authorized admission.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42k.py mechanism --attempt provisioned
```

## Current-run evidence

- [W-MECH-01/p42k-acceptance/result.json](../W-MECH-01/p42k-acceptance/result.json)
- [W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/admission.json](../W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/admission.json)
- [W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/restored-world.json](../W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/restored-world.json)
- [W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/smoke-witness.json](../W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/smoke-witness.json)
- [W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/undo.json](../W-MECH-01/p42k-stage-review-cold-20261007T193823.372400Z/undo.json)
- [W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/admission.json](../W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/admission.json)
- [W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/restored-world.json](../W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/restored-world.json)
- [W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/smoke-witness.json](../W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/smoke-witness.json)
- [W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/undo.json](../W-MECH-01/p42k-stage-review-warm-20261007T194244.401167Z/undo.json)
- [W-MECH-01/p42k-signed-record-20261007T194735.641371Z/verified.json](../W-MECH-01/p42k-signed-record-20261007T194735.641371Z/verified.json)
- [W-MECH-01/p42k-signed-record-20261007T194739.162762Z/verified.json](../W-MECH-01/p42k-signed-record-20261007T194739.162762Z/verified.json)
