# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **PASS**. Installed-main provider-before-budget 3D refusal returns not_configured; no generation. The named XML case passes.

Report timestamp: 2026-10-06T12:40:21.175384+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d guide
```

## Retained evidence

- [W-DOC-01/p42d-guide-20261006T123449.140659Z/3d-refusal.json](../W-DOC-01/p42d-guide-20261006T123449.140659Z/3d-refusal.json)
- [W-DOC-01/p42d-guide-20261006T123449.140659Z/results.xml](../W-DOC-01/p42d-guide-20261006T123449.140659Z/results.xml)

Exact acceptance/component cases: `P42c.Live.GuideTests.P42_OPS_01_Installed3dRefusesUnconfigured`. Component cases do not close any missing external workflow.

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
