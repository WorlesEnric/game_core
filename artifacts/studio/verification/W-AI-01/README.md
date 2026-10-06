# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **BLOCKED**. PARTIAL: R3-F six-digit tint applies/undoes with unchanged behaviour hashes; R3-A Sprite bind and R4-A image/TTS imports pass. Two images are generated, but the unchanged P3.2 robe driver never assigns its generated robe texture through R3-D entity.setMaterialTexture. The full texture-to-material chain is unexercised.

Report timestamp: 2026-10-06T15:29:44.397410+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c robe2
```

## Retained evidence

- [W-AI-01/p42c-robe2-20261006T072832.434641Z/README.md](../W-AI-01/p42c-robe2-20261006T072832.434641Z/README.md)
- [W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/robe/apply-report.json](../W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/robe/apply-report.json)
- [W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/icon/assign.json](../W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/icon/assign.json)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
