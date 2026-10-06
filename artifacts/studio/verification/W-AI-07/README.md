# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **PASS**. Installed R4-C provider-before-budget 3D refusal: not_configured surfaced, no generation.

Report timestamp: 2026-10-06T09:08:50.177799+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c guides
```

## Retained evidence

- [W-DOC-01/p42c-guides-20261006T082946.366496Z/results.xml](../W-DOC-01/p42c-guides-20261006T082946.366496Z/results.xml)
- [W-DOC-01/p42c-guides-20261006T082946.366496Z/3d-refusal.json](../W-DOC-01/p42c-guides-20261006T082946.366496Z/3d-refusal.json)

Exact acceptance/component cases: `P42c.Live.GuideTests.P42_OPS_01_Installed3dRefusesUnconfigured`. Component cases do not close any missing external workflow.

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
