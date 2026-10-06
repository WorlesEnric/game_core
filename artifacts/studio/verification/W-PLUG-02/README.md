# W-PLUG-02: Despawn/respawn keeps override; Animator bound

Verdict: **BLOCKED**. Despawn/respawn variant and scale override preservation passes; the required real Animator-binding part is not asserted by this suite.

Report timestamp: 2026-10-06T09:08:50.178675+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `WPlug02_DespawnRespawn_KeepsTheVariantAndScaleOverrides`. Component cases do not close any missing external workflow.
