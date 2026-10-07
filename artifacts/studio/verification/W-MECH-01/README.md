# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **PASS**. Current installed signed Docker cold/warm stages pass in 158.107/78.663s, each 36 EditMode+2 PlayMode XML passes. Creator Admit resumes real Play with nine coins and 120 Pending→Passed smoke frames in 56.613/33.879s; normal History undo succeeds twice. Initial cache_invalid setup failure is retained; exact owner-scoped pinned cache was provisioned without altering cold markers or budgets.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42i.py mechanism --attempt provisioned
```

## Current-run evidence

- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/outcome.json](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/outcome.json)
- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/restored-world.json](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/restored-world.json)
- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/smoke-witness.json](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/smoke-witness.json)
- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/admission.json](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/admission.json)
- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/undo.json](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/undo.json)
- [W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/resumed-world.png](../W-MECH-01/p42i-stage-review-cold-20261007T060148.783190Z/resumed-world.png)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/outcome.json](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/outcome.json)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/restored-world.json](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/restored-world.json)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/smoke-witness.json](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/smoke-witness.json)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/admission.json](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/admission.json)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/undo.json](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/undo.json)
- [W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/resumed-world.png](../W-MECH-01/p42i-stage-review-warm-20261007T060630.756770Z/resumed-world.png)
- [W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/job.json](../W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/job.json)
- [W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/slot-out/editmode.xml](../W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/slot-out/editmode.xml)
- [W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/slot-out/playmode.xml](../W-MECH-01/p42i-stage-submit-cold-20261007T055820.084238Z/service/slot-out/playmode.xml)
- [W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/job.json](../W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/job.json)
- [W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/slot-out/editmode.xml](../W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/slot-out/editmode.xml)
- [W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/slot-out/playmode.xml](../W-MECH-01/p42i-stage-submit-warm-20261007T060415.469534Z/service/slot-out/playmode.xml)
- [W-MECH-01/p42i-signed-record-20261007T060145.901277Z/verified.json](../W-MECH-01/p42i-signed-record-20261007T060145.901277Z/verified.json)
- [W-MECH-01/p42i-signed-record-20261007T060628.146841Z/verified.json](../W-MECH-01/p42i-signed-record-20261007T060628.146841Z/verified.json)
- [W-MECH-01/p42i-stage-submit-cold-20261007T054655.315450Z/service/job.json](../W-MECH-01/p42i-stage-submit-cold-20261007T054655.315450Z/service/job.json)
- [W-MECH-01/p42i-cache-provision/provision.json](../W-MECH-01/p42i-cache-provision/provision.json)
- [W-MECH-01/p42i-stage-negative-review-20261007T061419.738746Z/panel-verdict.json](../W-MECH-01/p42i-stage-negative-review-20261007T061419.738746Z/panel-verdict.json)
- [W-MECH-01/p42i-stage-submit-negative-20261007T061309.881587Z/service/job.json](../W-MECH-01/p42i-stage-submit-negative-20261007T061309.881587Z/service/job.json)
