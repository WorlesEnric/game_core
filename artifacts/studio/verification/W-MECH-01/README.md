# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **PASS — R10-A**, product `2e17a01ec02dcea3b4762b4536605120c985681b`, locally built scratch companion, graphical `:1`. Signed cold/warm stages take **165.852 / 82.232 s**; creator Admit → restored Play/smoke takes **34.751 / 30.089 s**, below the unchanged 90 s limit. Normal History Undo takes **36.494 / 33.832 s**. Both restore nine coins and run 120 real frames with Pending → Passed smoke, authenticated catalog equality, removed package and no pending state after Undo.

Both flows first exercise a worker-format structural edit through candidate Preview/Apply, observe early `bake_stale` while generated runtime is incompatible, then normal journal Undo and current snapshot export with byte-identical restored assets and unchanged bake outputs. No paid worker is called. The legacy pressure sample is observer-only: actual restored game root equals signed world; independently loaded world+mechanism catalog set equals signed predicted.

Current evidence: [cold result](r10-a/final/cold/result.json), [warm result](r10-a/final/warm/result.json), [qualification/XML counts](r10-a/qualification.json), [packet](../../../../docs/studio/packets/R10-A-admission-freshness.md). Each run includes exact signed records, real stage XML, actual Admit-control pixels, source/Undo witnesses, terminal journals and SHA-256 manifest. No installed companion/etosd operation or installed-release upgrade is claimed.

Reproduce: `python3 games/hollowmere/Assets/Hollowmere/Tests/R10_A/run.py /tmp/r10-a-new --label new --companion "$PWD/studio/agent/target/debug/gamecore-studio"` after building that companion and the Release literal-submit client. Use fresh private state and evidence labels; never overwrite retained attempts.

## Historical P4.2j failure

Both then-current signed Docker stages passed (166.402s cold, 76.786s warm), but creator admissions rolled back with `catalog_mismatch`: signed world `6c13778e` versus current authored `d82aed18`. Historical product `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release `0.1.0-fba3604e99ceadd1`; reported 2026-10-07T16:04:44.851949+00:00. Its original failure records below remain unchanged.

### Historical reproduction

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42j.py mechanism; after verified cold rollback only: python3 artifacts/studio/verification/TOOLS/stages-p42j.py mechanism --mechanism-run warm
```

### Historical evidence

- [W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/outcome.json](../W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/outcome.json)
- [W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/outcome.json](../W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/outcome.json)
- [W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/admission.json](../W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/admission.json)
- [W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/admission.json](../W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/admission.json)
