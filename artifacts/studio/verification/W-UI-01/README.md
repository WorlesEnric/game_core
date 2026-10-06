# W-UI-01: Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition

Verdict: **FAIL**. Graphical walking/picking captured and 1280×720 fits, but automatic gateway startup and R2-29 keyboard-event delivery fail; explicit pairing does not close the ordinary open-and-play path.

Report timestamp: 2026-10-06T04:56:25.153017+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh ui
studio/tools/verify-all.sh graphics
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
- [W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md](../W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md)
- [GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md](../GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md)
