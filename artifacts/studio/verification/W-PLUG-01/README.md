# W-PLUG-01: Three-region loop, moved NPC stays, memory baseline, timings

Verdict: **BLOCKED**. Real three-region travel/pose/residency tests pass. Region texture/audio release and the required peak-relative Memory Profiler delta remain unmeasured.

Report timestamp: 2026-10-05T21:03:25.256734+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)
- [B-REGION/README.md](../B-REGION/README.md)

Exact acceptance/component cases: `TravelsVillageMarshBelfryVillage`. Component cases do not close any missing external workflow.
