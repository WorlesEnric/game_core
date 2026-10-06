# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **PASS**. Installed R6-main companion refuses 3D with not_configured before a zero budget; no generation. The authenticated P4.2e receipt passes in TRX. FBX-only mesh import and predictions provider credentials remain an explicit owner decision.

Report timestamp: 2026-10-06T15:29:44.408093+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py receipt-hello
```

## Retained evidence

- [INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json](../INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json)
- [INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/3d-refusal.json](../INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/3d-refusal.json)

Exact acceptance/component cases: `R2_38_P42e_HelloOwnerDescribeAnd3dRefusal`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
