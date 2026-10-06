# W-ETOS-08: Remove image provider: `not_configured` surfaced; nothing applied

Verdict: **BLOCKED**. Removing the shared image provider and reloading the node would change concurrent users’ provider service; that operator scenario is outside the permitted no-node-restart run.

Report timestamp: 2026-10-06T12:40:21.168751+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh media
```

## Retained evidence

- [W-HOST-01/immutable-install-20261005T185516.633891Z/README.md](../W-HOST-01/immutable-install-20261005T185516.633891Z/README.md)
