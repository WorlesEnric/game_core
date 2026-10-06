# W-UI-01: Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition

Verdict: **PASS**. Ordinary graphical Open Studio connects automatically without pairing; HUD walking moves committed world.posX/posZ 7.636 m in 3.040 s. Runtime NPC view tags now override the authored GameBoot ancestor; Select shows Maren and MarenEntity in the actual definition card. Graphical R2-29 key delivery and focus-reset regressions pass.

Source: `1a462f88`. [R7-A proof and exact limitations](r7-a/README.md).

## Reproduce

```sh
bash artifacts/studio/verification/W-UI-01/r7-a/run.sh
```

## Retained evidence

- [W-UI-01/r7-a/README.md](../W-UI-01/r7-a/README.md)
- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
- [W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md](../W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md)
- [GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md](../GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md)
