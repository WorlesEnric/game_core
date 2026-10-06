# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **BLOCKED**. P4.2e authenticated hello and one describe call pass with the owner-declared operator estimate USD 0.01/call. One new image is generated and downloaded with digest verification at USD 0.20; describe consumes that owned artifact and records its binding charge. Current client tests cover tamper refusal, but this run does not perform the full live generated-texture import/tamper workflow; prior image-import evidence remains revision-specific.

Report timestamp: 2026-10-06T15:29:44.392368+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py receipt-hello; python3 artifacts/studio/verification/TOOLS/live-p42e.py describe
```

## Retained evidence

- [W-ETOS-07/p42e-describe-20261006T143956.171469Z/result.json](../W-ETOS-07/p42e-describe-20261006T143956.171469Z/result.json)
- [W-ETOS-07/p42e-describe-20261006T143956.171469Z/describe.json](../W-ETOS-07/p42e-describe-20261006T143956.171469Z/describe.json)
- [INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json](../INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json)

Exact acceptance/component cases: `R2_38_P42e_OnePricedImageThenOneDescribe`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
