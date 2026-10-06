# W-PLUG-04: Patrol index across unload/reload and save/load

Verdict: **PASS**. Fresh-boot restore retains the unloaded NPC’s captured slots, including PatrolIndex, and gameplay patrol tests assert committed progression.

Report timestamp: 2026-10-06T04:56:25.173958+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)
- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `FreshBootRestore_ReattachesTheNarrativeLayer_AndDeliversInFlightWorkExactlyOnce`, `NpcPatrol_CommitsWhatTheLogicalMoverComputes_AndRaisesNpcArrived`. Component cases do not close any missing external workflow.
