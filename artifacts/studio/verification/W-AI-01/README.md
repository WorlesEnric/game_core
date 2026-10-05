# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **BLOCKED**. The image tariff remains unverified and manual media request ownership is broken; no current generated robe/material/undo workflow completed. R3 texture-binding regression passes only as component evidence.

Report timestamp: 2026-10-05T21:03:25.254654+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [W-ETOS-07/installed-media-20261005T192218.561813Z/README.md](../W-ETOS-07/installed-media-20261005T192218.561813Z/README.md)
- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `D9_ImportedTextureTypedChangeSetUndoRedoAndBakeImpact`. Component cases do not close any missing external workflow.
