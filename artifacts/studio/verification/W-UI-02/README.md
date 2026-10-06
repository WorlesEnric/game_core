# W-UI-02: Box-select three NPCs behind a fence; overlap list; choose NPCs

Verdict: **BLOCKED**. The capture includes marquee/overlap UI, but not the required three NPCs behind a fence with the resulting overlap choices.

Report timestamp: 2026-10-06T15:29:44.363294+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh ui
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
