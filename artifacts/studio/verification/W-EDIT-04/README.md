# W-EDIT-04: Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget`

Verdict: **BLOCKED**. Stale-target pieces pass, but the exact live NPC → exit Play → apply → delete → apply sequence is not retained as a combined acceptance run.

Report timestamp: 2026-10-06T09:08:50.159657+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
