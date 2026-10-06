# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. Installed app-origin stage passes all seven gates in 158.122 s (33 EditMode + 2 PlayMode XML passes). Authenticated fetch/verify enables Admit and the visible badge says verdict pass. Explicit Play Admit captures/stops and passes the real catalog checkpoint, but compilation stalls; the owned wrapper is stopped at 817 s. Recovery finishes rollback with catalog_mismatch because the signed delta has no world/predicted hashes. Package and pending record are absent afterward. No live restoration, tri-state smoke or successful admission undo is claimed. The semantic-negative fixture is refused with 14 lexical hits, no issued passing verdict and Admit disabled; Roslyn/Unity are skipped for that refusal.

Report timestamp: 2026-10-06T12:40:21.183430+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
See artifacts/studio/verification/TOOLS/README-P4.2d.md: fresh identity, cache provision, stage-submit, watch-stage-p42c.py, stage-review, stage-recover.
```

## Retained evidence

- [W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/job.json](../W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/job.json)
- [W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/slot-out/editmode.xml](../W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/slot-out/editmode.xml)
- [W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/slot-out/playmode.xml](../W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/service/slot-out/playmode.xml)
- [W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z/panel-verdict.json](../W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z/panel-verdict.json)
- [W-MECH-01/p42d-stage-review-20261006T120733.384847Z/play-verification.json](../W-MECH-01/p42d-stage-review-20261006T120733.384847Z/play-verification.json)
- [W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/admission.json](../W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/admission.json)
- [W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/outcome.json](../W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/outcome.json)
- [W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z/verified.json](../W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z/verified.json)
- [W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z/signed-record.json](../W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z/signed-record.json)
- [W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z/panel-verdict.json](../W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z/panel-verdict.json)
- [W-MECH-01/p42d-stage-submit-negative-20261006T123038.941727Z/service/job.json](../W-MECH-01/p42d-stage-submit-negative-20261006T123038.941727Z/service/job.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
