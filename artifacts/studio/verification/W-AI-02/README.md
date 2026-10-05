# W-AI-02: Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav

Verdict: **BLOCKED**. The packet’s one bounded text task produced a rejected well move. Ferryman dialogue/patrol/NavMesh behaviour in Play remains historical/partial P3.2 evidence, not a current end-to-end run.

Report timestamp: 2026-10-05T21:03:25.254994+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
